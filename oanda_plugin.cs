using System;
using System.Runtime.Remoting.Contexts;
using System.Drawing.Design;
using System.ComponentModel;
using System.IO;
using System.Globalization;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Forms.Design;
using System.Xml.Serialization;
using System.Runtime.Serialization;

using RightEdge.Common;

using fxClientAPI;


/******************************************************************
*******************************************************************
TODO :
 
 * SetAccountState()
    * remove manual liveopenposition.xml parsing
    * what happens when there's an open order???
    * what happens when a re-synch'd order is hit (p stops/targets/close and o.open)??? 

 * "object disposed" exception :
   there may be some code left that does not properly refetch the account object
   when the out channel is reconnected due to an oanda exception
   
 * fix cross price pair selection logic and calculation math
 
 * be sure all bounds violation events from oanda are handled
   market opens, limit fills, stop/target fills, etc...
 
 * finish generalizing the custom comparable dictionary form
 
WISH LIST :
 
 * full 2-way position sync between right edge and oanda :
   requires the ability to initiate orders into the rightedge space from the broker plugin.
   Currently right edge does not allow this behaviour and will throw an exception.
   
   There is some code to handle the following situations, but they will not be fully
   operational until right edge supports order creation from the broker plugin.
   
   * long/short order pairing : the opposing open can be handled but this also requires
     initiating at least one close order.
   * oanda client initiated events effecting right edge managed positions
*******************************************************************
*******************************************************************/

namespace RightEdgeOandaPlugin
{
    public interface INamedObject
    {
        string Name { get; }
        void SetName(string n);
    }
    public class NamedConverter : TypeConverter
    {
        public override object ConvertTo(
                 ITypeDescriptorContext context,
                 CultureInfo culture,
                 object value,
                 Type destinationType)
        {
            if (destinationType == typeof(string))
            {
                if (value is INamedObject)
                {
                    return ((INamedObject)value).Name;
                }
                else
                {
                    return "";
                }
            }

            return base.ConvertTo(
                context,
                culture,
                value,
                destinationType);
        }
    }

    #region XML Dictionary Serialization
    /// http://weblogs.asp.net/pwelter34/archive/2006/05/03/444961.aspx
    /// Author : Paul Welter (pwelter34)
    [XmlRoot("dictionary")]
    public class SerializableDictionary<TKey, TValue>
        : Dictionary<TKey, TValue>, IXmlSerializable
    {
        public SerializableDictionary() { }
        public SerializableDictionary(IEqualityComparer<TKey> comp) : base(comp) { }

        #region IXmlSerializable Members
        public System.Xml.Schema.XmlSchema GetSchema() { return null; }

        public void ReadXml(System.Xml.XmlReader reader)
        {
            XmlSerializer keySerializer = new XmlSerializer(typeof(TKey));
            XmlSerializer valueSerializer = new XmlSerializer(typeof(TValue));
            bool wasEmpty = reader.IsEmptyElement;
            reader.Read();
            if (wasEmpty) { return; }
            while (reader.NodeType != System.Xml.XmlNodeType.EndElement)
            {
                reader.ReadStartElement("item");
                reader.ReadStartElement("key");
                TKey key = (TKey)keySerializer.Deserialize(reader);
                reader.ReadEndElement();
                reader.ReadStartElement("value");
                TValue value = (TValue)valueSerializer.Deserialize(reader);
                reader.ReadEndElement();
                this.Add(key, value);
                reader.ReadEndElement();
                reader.MoveToContent();
            }
            reader.ReadEndElement();
        }

        public void WriteXml(System.Xml.XmlWriter writer)
        {
            XmlSerializer keySerializer = new XmlSerializer(typeof(TKey));
            XmlSerializer valueSerializer = new XmlSerializer(typeof(TValue));

            foreach (TKey key in this.Keys)
            {
                writer.WriteStartElement("item");
                writer.WriteStartElement("key");
                keySerializer.Serialize(writer, key);
                writer.WriteEndElement();
                writer.WriteStartElement("value");
                TValue value = this[key];
                valueSerializer.Serialize(writer, value);
                writer.WriteEndElement();
                writer.WriteEndElement();
            }
        }
        #endregion
    }
    #endregion

    #region OAPluginException
    /// <summary>
    /// Custom exception.
    /// </summary>
    [Serializable]
    public class OAPluginException : Exception, ISerializable
    {
        #region Local private members
        protected DateTime _dateTime = DateTime.Now;
        protected String _machineName = Environment.MachineName;
        protected String _exceptionType = "";
        private String _exceptionDescription = "";
        protected String _stackTrace = "";
        protected String _assemblyName = "";
        protected String _messageName = "";
        protected String _messageId = "";
        protected Hashtable _data = null;
        protected String _source = "";
        #endregion

        #region ctors
        public OAPluginException()
            : base()
        {
            if (Environment.StackTrace != null)
                this._stackTrace = Environment.StackTrace;
        }

        public OAPluginException(String message)
            : base(message)
        {
            if (Environment.StackTrace != null)
                this._stackTrace = Environment.StackTrace;
        }

        public OAPluginException(String message, Exception innerException) :
            base(message, innerException)
        {
            if (Environment.StackTrace != null)
                this._stackTrace = Environment.StackTrace;
        }

        public OAPluginException(String message, Exception innerException, String messageName, String mqMessageId) :
            base(message, innerException)
        {
            this._messageId = mqMessageId;
            this._messageName = messageName;
            if (Environment.StackTrace != null)
                this._stackTrace = Environment.StackTrace;
        }

        public OAPluginException(String message, Exception innerException, String messageName, String mqMessageId, String source) :
            base(message, innerException)
        {
            this._messageId = mqMessageId;
            this._messageName = messageName;
            this._source = source.Equals("") ? this._source : source;
            if (Environment.StackTrace != null)
                this._stackTrace = Environment.StackTrace;
        }
        #endregion

        #region ISerializable members

        /// <summary>
        /// This CTor allows exceptions to be marhalled accross remoting boundaries
        /// </summary>
        /// <param name="info"></param>
        /// <param name="context"></param>
        protected OAPluginException(SerializationInfo info, StreamingContext context) :
            base(info, context)
        {
            this._dateTime = info.GetDateTime("_dateTime");
            this._machineName = info.GetString("_machineName");
            this._stackTrace = info.GetString("_stackTrace");
            this._exceptionType = info.GetString("_exceptionType");
            this._assemblyName = info.GetString("_assemblyName");
            this._messageName = info.GetString("_messageName");
            this._messageId = info.GetString("_messageId");
            this._exceptionDescription = info.GetString("_exceptionDescription");
            this._data = (Hashtable)info.GetValue("_data", Type.GetType("System.Collections.Hashtable"));
        }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue("_dateTime", this._dateTime);
            info.AddValue("_machineName", this._machineName);
            info.AddValue("_stackTrace", this._stackTrace);
            info.AddValue("_exceptionType", this._exceptionType);
            info.AddValue("_assemblyName", this._assemblyName);
            info.AddValue("_messageName", this._messageName);
            info.AddValue("_messageId", this._messageId);
            info.AddValue("_exceptionDescription", this._exceptionDescription);
            info.AddValue("_data", this._data, Type.GetType("System.Collections.Hashtable"));
            base.GetObjectData(info, context);
        }

        #endregion
    }
    #endregion

    #region result classes
    public class FunctionResult
    {
        public FunctionResult() { }
        public FunctionResult(FunctionResult src) { _error = src._error; _message = src._message; }

        private bool _error = false;
        public bool Error { set { _error = value; } get { return (_error); } }

        private string _message = string.Empty;
        public string Message { set { _message = value; } get { return (_message); } }

        public void setError(string m) { _message = m; _error = true; }
        public void clearError() { _message = string.Empty; _error = false; }

        public static FunctionResult newError(string m) { FunctionResult fr = new FunctionResult(); fr.setError(m); return fr; }
    }
    public class FunctionObjectResult<T> : FunctionResult
    {
        public FunctionObjectResult() { }
        public FunctionObjectResult(FunctionObjectResult<T> src) : base(src) { ResultObject = src.ResultObject; }

        public T ResultObject = default(T);
    }

    public class TaskResult : FunctionResult
    {
        public TaskResult() { }
        public TaskResult(FunctionResult fr) : base(fr) { }
        public bool TaskCompleted = false;
    }
    public class TaskObjectResult<T> : FunctionObjectResult<T>
    {
        public TaskObjectResult() { }
        public TaskObjectResult(FunctionObjectResult<T> fr) : base(fr) { }

        public bool TaskCompleted = false;
    }

    public interface IFXClientResponse
    {
        bool Error { set; get; }
        string Message { set; get; }
        FXClientResponseType FXClientResponse { set; get; }
        bool Disconnected { set; get; }
        bool OrderMissing { set; get; }
        int OrdersSent { set; get; }
        void setError(string m, FXClientResponseType cresp);
        void setError(string m, FXClientResponseType cresp, bool discon);
    }

    public enum FXClientResponseType { Invalid, Rejected, Accepted, Disconnected };
    public class FXClientObjectResult<T> : FunctionObjectResult<T>, IFXClientResponse
    {
        private FXClientResponseType _response = FXClientResponseType.Invalid;
        public FXClientResponseType FXClientResponse { set { _response = value; } get { return (_response); } }

        private bool _discon = false;
        public bool Disconnected { set { _discon = value; } get { return (_discon); } }

        private bool _order_missing = false;
        public bool OrderMissing { set { _order_missing = value; } get { return (_order_missing); } }

        private int _orders_sent = 0;
        public int OrdersSent { set { _orders_sent = value; } get { return (_orders_sent); } }

        public void setError(string m, FXClientResponseType cresp)
        {
            FXClientResponse = cresp;
            base.setError(m);
        }
        public void setError(string m, FXClientResponseType cresp, bool discon)
        {
            FXClientResponse = cresp;
            Disconnected = discon;
            base.setError(m);
        }
    }
    public class FXClientResult : FunctionResult, IFXClientResponse
    {
        private FXClientResponseType _response = FXClientResponseType.Invalid;
        public FXClientResponseType FXClientResponse { set { _response = value; } get { return (_response); } }

        private bool _discon = false;
        public bool Disconnected { set { _discon = value; } get { return (_discon); } }

        private bool _order_missing = false;
        public bool OrderMissing { set { _order_missing = value; } get { return (_order_missing); } }

        private int _orders_sent = 0;
        public int OrdersSent { set { _orders_sent = value; } get { return (_orders_sent); } }

        public void setError(string m, FXClientResponseType cresp)
        {
            FXClientResponse = cresp;
            base.setError(m);
        }
        public void setError(string m, FXClientResponseType cresp, bool discon)
        {
            FXClientResponse = cresp;
            Disconnected = discon;
            base.setError(m);
        }
    }
    public class FXClientTaskResult : TaskResult, IFXClientResponse
    {
        private FXClientResponseType _response = FXClientResponseType.Invalid;
        public FXClientResponseType FXClientResponse { set { _response = value; } get { return (_response); } }

        private bool _discon = false;
        public bool Disconnected { set { _discon = value; } get { return (_discon); } }

        private bool _order_missing = false;
        public bool OrderMissing { set { _order_missing = value; } get { return (_order_missing); } }

        private int _orders_sent = 0;
        public int OrdersSent { set { _orders_sent = value; } get { return (_orders_sent); } }

        public void setError(string m, FXClientResponseType cresp)
        {
            FXClientResponse = cresp;
            base.setError(m);
        }
        public void setError(string m, FXClientResponseType cresp, bool discon)
        {
            FXClientResponse = cresp;
            Disconnected = discon;
            base.setError(m);
        }
    }

    public class FXClientTaskObjectResult<T> : TaskObjectResult<T>,IFXClientResponse
    {
        private FXClientResponseType _response = FXClientResponseType.Invalid;
        public FXClientResponseType FXClientResponse { set { _response = value; } get { return (_response); } }

        private bool _discon = false;
        public bool Disconnected { set { _discon = value; } get { return (_discon); } }

        private bool _order_missing = false;
        public bool OrderMissing { set { _order_missing = value; } get { return (_order_missing); } }

        private int _orders_sent = 0;
        public int OrdersSent { set { _orders_sent = value; } get { return (_orders_sent); } }

        public void setError(string m, FXClientResponseType cresp)
        {
            FXClientResponse = cresp;
            base.setError(m);
        }
        public void setError(string m, FXClientResponseType cresp, bool discon)
        {
            FXClientResponse = cresp;
            Disconnected = discon;
            base.setError(m);
        }
    }

    public class PositionFetchResult : FunctionObjectResult<BrokerPositionRecord>
    {
        public bool PositionExists { get { return (ResultObject != null); } }

        public int AccountId;
        public string SymbolName;
        public string PositionId;
    }

    public enum TransactionMatchType { None, Trans, Link, Position };

    public class TransactionFetchResult : PositionFetchResult
    {
        public bool IsLinked = false;
        public IDString OrderId = null;
        public TradeRecord TransactionTradeRecord = null;
    }

    public class TransactionRecordResult : FunctionResult
    {
        public bool IsLinked { get { return (MatchType == TransactionMatchType.Link); } }

        public int AccountId;
        public string SymbolName;

        public string PositionId;
        public IDString OrderId = null;

        public TransactionMatchType MatchType = TransactionMatchType.None;
        public bool PositionExists = false;
    }

    public class AccountResult
    {
        public Account FromInChannel = null;
        public Account FromOutChannel = null;
    }
    #endregion

    #region General utilities static class
    public enum CustomBarFrequency { Tick, OneMinute, FiveMinute, TenMinute, FifteenMinute, ThirtyMinute, SixtyMinute, ThreeHour, FourHour, Daily, Weekly, Monthly, Yearly }
    public static class OandAUtils
    {
        public static CustomBarFrequency convertToCustomBarFrequency(int f)
        {
            switch (f)
            {
                case 1: return (CustomBarFrequency.OneMinute);
                case 5: return (CustomBarFrequency.FiveMinute);
                case 10: return (CustomBarFrequency.TenMinute);
                case 15: return (CustomBarFrequency.FifteenMinute);
                case 30: return (CustomBarFrequency.ThirtyMinute);
                case 60: return (CustomBarFrequency.SixtyMinute);
                case 1440: return (CustomBarFrequency.Daily);
                case 10080: return (CustomBarFrequency.Weekly);
                default:
                    throw (new OAPluginException("convertToCustomBarFrequency() unknown frequency value"));
            }

        }
        public static DateTime frequencyIncrementTime(DateTime t, CustomBarFrequency f, int increments)
        {
            DateTime xt;
            switch (f)
            {
                case CustomBarFrequency.OneMinute:
                    xt = t.AddMinutes(1 * increments);
                    break;
                case CustomBarFrequency.FiveMinute:
                    xt = t.AddMinutes(5 * increments);
                    break;
                case CustomBarFrequency.TenMinute:
                    xt = t.AddMinutes(10 * increments);
                    break;
                case CustomBarFrequency.FifteenMinute:
                    xt = t.AddMinutes(15 * increments);
                    break;
                case CustomBarFrequency.ThirtyMinute:
                    xt = t.AddMinutes(30 * increments);
                    break;
                case CustomBarFrequency.SixtyMinute:
                    xt = t.AddMinutes(60 * increments);
                    break;
                case CustomBarFrequency.ThreeHour:
                    xt = t.AddHours(3 * increments);
                    break;
                case CustomBarFrequency.FourHour:
                    xt = t.AddHours(4 * increments);
                    break;
                case CustomBarFrequency.Daily:
                    xt = t.AddDays(1 * increments);
                    break;
                case CustomBarFrequency.Weekly:
                    xt = t.AddDays(7 * increments);
                    break;
                case CustomBarFrequency.Monthly:
                    xt = t.AddMonths(1 * increments);
                    break;
                case CustomBarFrequency.Yearly:
                    xt = t.AddYears(1 * increments);
                    break;
                default:
                    throw (new OAPluginException("frequencyIncrementTime() unknown time frequency."));
            }
            return (xt);
        }
        public static DateTime frequencyRoundToStart(DateTime t, CustomBarFrequency f)
        {
            double v;
            DateTime rt;
            switch (f)
            {
                case CustomBarFrequency.OneMinute:
                    rt = t.AddSeconds(-1.0 * t.Second);
                    break;
                case CustomBarFrequency.FiveMinute:
                    v = -1.0 * ((t.Minute % 5.0) + (t.Second / 60.0));
                    rt = t.AddMinutes(v);
                    break;
                case CustomBarFrequency.TenMinute:
                    v = -1.0 * ((t.Minute % 10.0) + (t.Second / 60.0));
                    rt = t.AddMinutes(v);
                    break;
                case CustomBarFrequency.FifteenMinute:
                    v = -1.0 * ((t.Minute % 15.0) + (t.Second / 60.0));
                    rt = t.AddMinutes(v);
                    break;
                case CustomBarFrequency.ThirtyMinute:
                    v = -1.0 * ((t.Minute % 30.0) + (t.Second / 60.0));
                    rt = t.AddMinutes(v);
                    break;
                case CustomBarFrequency.SixtyMinute:
                    v = -1.0 * ((t.Minute % 60.0) + (t.Second / 60.0));
                    rt = t.AddMinutes(v);
                    break;
                case CustomBarFrequency.ThreeHour:
                    v = -1.0 * ((t.Hour % 3.0) + ((t.Minute + (t.Second / 60.0)) / 60.0));
                    rt = t.AddHours(v);
                    break;
                case CustomBarFrequency.FourHour:
                    v = -1.0 * ((t.Hour % 4.0) + ((t.Minute + (t.Second / 60.0)) / 60.0));
                    rt = t.AddHours(v);
                    break;
                case CustomBarFrequency.Daily:
                    v = -1.0 * ((t.Hour % 24.0) + ((t.Minute + (t.Second / 60.0)) / 60.0));
                    rt = t.AddHours(v);
                    break;
                case CustomBarFrequency.Weekly:
                    throw (new OAPluginException("frequencyRoundToStart() frequency TBI."));
                //break;
                case CustomBarFrequency.Monthly:
                    throw (new OAPluginException("frequencyRoundToStart() frequency TBI."));
                //break;
                case CustomBarFrequency.Yearly:
                    throw (new OAPluginException("frequencyRoundToStart() frequency TBI."));
                //break;
                default:
                    throw (new OAPluginException("frequencyRoundToStart() unknown time frequency."));
            }
            return (rt);
        }

        public static BarData convertBarData(fxHistoryPoint hpoint)
        {
            CandlePoint cpoint = hpoint.GetCandlePoint();
            BarData nb = new BarData();

            nb.PriceDateTime = cpoint.Timestamp;
            nb.Open = cpoint.Open;
            nb.Close = cpoint.Close;
            nb.High = cpoint.Max;
            nb.Low = cpoint.Min;

            if (nb.Open == nb.Close && nb.Open == nb.High && nb.Open == nb.Low)
            {
                nb.Volume = 0;
                nb.EmptyBar = true;
            }
            else { nb.Volume = 1; }

            nb.OpenInterest = 0;

            //calculate the maximum spread encountered
            double sp = hpoint.Open.Ask - hpoint.Open.Bid;
            double v = hpoint.Min.Ask - hpoint.Min.Bid;
            if (v > sp) { sp = v; }
            v = hpoint.Max.Ask - hpoint.Max.Bid;
            if (v > sp) { sp = v; }
            v = hpoint.Close.Ask - hpoint.Close.Bid;
            if (v > sp) { sp = v; }

            //setup bid/ask off the candle close and the largest detected spread
            nb.Bid = cpoint.Close - (0.5 * sp);
            nb.Ask = cpoint.Close + (0.5 * sp);

            return (nb);
        }
        public static TickData convertTicks(fxTick fx_tick)
        {
            TickData nt = new TickData();
            nt.time = fx_tick.Timestamp;
            nt.size = 1;
            return (nt);
        }
        public static TickData convertTicks_trade(fxTick fx_tick)
        {
            TickData nt = new TickData();
            nt.time = fx_tick.Timestamp;
            nt.price = fx_tick.Mean;
            nt.size = 1;
            nt.tickType = TickType.Trade;
            return (nt);
        }
        public static TickData convertTicks_ask(fxTick fx_tick)
        {
            TickData nt = new TickData();
            nt.time = fx_tick.Timestamp;
            nt.price = fx_tick.Ask;
            nt.size = 1;
            nt.tickType = TickType.Ask;
            return (nt);
        }
        public static TickData convertTicks_bid(fxTick fx_tick)
        {
            TickData nt = new TickData();
            nt.time = fx_tick.Timestamp;
            nt.price = fx_tick.Bid;
            nt.size = 1;
            nt.tickType = TickType.Bid;
            return (nt);
        }
        public static List<int> supportedIntervals()
        {
            List<int> list = new List<int>();
            list.Add((int)BarFrequency.OneMinute);
            list.Add((int)BarFrequency.FiveMinute);
            list.Add((int)BarFrequency.FifteenMinute);
            list.Add((int)BarFrequency.ThirtyMinute);
            list.Add((int)BarFrequency.SixtyMinute);
            list.Add((int)BarFrequency.Daily);
            return (list);
        }
        public static Interval convertToInterval(int fi)
        {
            switch (fi)
            {
                case 1: return Interval.Every_Minute;
                case 5: return Interval.Every_5_Minutes;
                case 15: return Interval.Every_15_Minutes;
                case 30: return Interval.Every_30_Minutes;
                case 60: return Interval.Every_Hour;
                case 1440: return Interval.Every_Day;
                default:
                    throw (new OAPluginException("Unable to convert integer input frequency value '" + fi + "'."));
            }
        }
        public static int convertFrequencyInterval(Interval fi)
        {
            switch (fi)
            {
                case Interval.Every_5_Seconds: throw (new OAPluginException("Interval not supported"));
                case Interval.Every_10_Seconds: throw (new OAPluginException("Interval not supported"));
                case Interval.Every_30_Seconds: throw (new OAPluginException("Interval not supported"));
                case Interval.Every_Minute: return (int)BarFrequency.OneMinute;
                case Interval.Every_5_Minutes: return (int)BarFrequency.FiveMinute;
                case Interval.Every_15_Minutes: return (int)BarFrequency.FifteenMinute;
                case Interval.Every_30_Minutes: return (int)BarFrequency.ThirtyMinute;
                case Interval.Every_Hour: return (int)BarFrequency.SixtyMinute;
                case Interval.Every_3_Hours: throw (new OAPluginException("Interval not supported"));
                case Interval.Every_Day: return (int)BarFrequency.Daily;
                default:
                    throw (new OAPluginException("Unable to convert Interval input frequency value '" + fi + "'."));
            }
        }

        public static PositionType convertToPositionType(TransactionType transactionType)
        {
            switch (transactionType)
            {
                case TransactionType.Buy: return PositionType.Long;
                case TransactionType.Sell: return PositionType.Long;
                case TransactionType.Short: return PositionType.Short;
                case TransactionType.Cover: return PositionType.Short;
                default:
                    throw new OAException("Unable to convert transaction type '" + transactionType + "' to position type.");
            }
        }
    }
    #endregion

    #region LiveOpenPositions serialization classes
    [Serializable]
    public class TradeOrderXml
    {
        private string _o_id = string.Empty;
        public string OrderID { set { _o_id = value; } get { return _o_id; } }

        private string _pos_id = string.Empty;
        public string PosID { set { _pos_id = value; } get { return _pos_id; } }

        private TradeType _tt = TradeType.None;
        public TradeType TradeType { set { _tt = value; } get { return _tt; } }
        
        private string _desc = string.Empty;
        public string Description { set { _desc = value; } get { return _desc; } }

        private int _bars_valid = -1;
        public int BarsValid { set { _bars_valid = value; } get { return _bars_valid; } }

        private bool _pending_cancel = false;
        public bool CancelPending { set { _pending_cancel = value; } get { return _pending_cancel; } }
    }

    [Serializable]
    public class PositionDataXml
    {
        private string _pos_id = string.Empty;
        public string PosID { set { _pos_id = value; } get { return _pos_id; } }

        private Symbol _sym = null;
        public Symbol Symbol { set { _sym = value; } get { return _sym; } }

        private PositionType _pt = PositionType.Long;
        public PositionType PositionType { set { _pt = value; } get { return _pt; } }

        private string _desc = string.Empty;
        public string Description { set { _desc = value; } get { return _desc; } }

        private List<TradeInfo> _trades = new List<TradeInfo>();
        public List<TradeInfo> Trades { set { _trades = value; } get { return _trades; } }

        private List<TradeOrderXml> _pending = new List<TradeOrderXml>();
        public List<TradeOrderXml> PendingOrders { set { _pending = value; } get { return _pending; } }

        private double _ptarget = 0.0;
        public double ProfitTarget { set { _ptarget = value; } get { return _ptarget; } }

        private double _ptarget_price = 0.0;
        public double ProfitTargetPrice { set { _ptarget_price = value; } get { return _ptarget_price; } }

        private TargetPriceType _ptarget_type = TargetPriceType.None;
        public TargetPriceType ProfitTargetType { set { _ptarget_type = value; } get { return _ptarget_type; } }

        private double _sloss = 0.0;
        public double StopLoss { set { _sloss = value; } get { return _sloss; } }

        private double _sloss_price = 0.0;
        public double StopLossPrice { set { _sloss_price = value; } get { return _sloss_price; } }

        private TargetPriceType _sloss_type = TargetPriceType.None;
        public TargetPriceType StopLossType { set { _sloss_type = value; } get { return _sloss_type; } }

        private double _tstop = 0.0;
        public double TrailingStop { set { _tstop = value; } get { return _tstop; } }

        private TargetPriceType _tstop_type = TargetPriceType.None;
        public TargetPriceType TrailingStopType { set { _tstop_type = value; } get { return _tstop_type; } }

        private int _bar_count_exit = -1;
        public int BarCountExit { set { _bar_count_exit = value; } get { return _bar_count_exit; } }

        private bool _pending_close = false;
        public bool PendingClose { set { _pending_close = value; } get { return _pending_close; } }

        private bool _is_pending = false;
        public bool IsPending { set { _is_pending = value; } get { return _is_pending; } }

        /*
        public override FunctionResult initFromSerialized<T>(T v)
        {
            if (typeof(T) == typeof(PositionDataXml))
            {
                PositionDataXml src = (PositionDataXml)((object)v);
                _bar_count_exit = src.BarCountExit;
                _desc = src.Description;
                _is_pending = src.IsPending;
                _pending = src.PendingOrders;
                _pending_close = src.PendingClose;
                _pos_id = src.PosID;
                _pt = src.PositionType;
                _ptarget = src.ProfitTarget;
                _ptarget_price = src.ProfitTargetPrice;
                _ptarget_type = src.ProfitTargetType;
                _sloss = src.StopLoss;
                _sloss_price = src.StopLossPrice;
                _sloss_type = src.StopLossType;
                _sym = src.Symbol;
                _trades = src.Trades;
                _tstop = src.TrailingStop;
                _tstop_type = src.TrailingStopType;
            }
            return base.initFromSerialized<T>(v);
        }
        public override FunctionResult initFromSerialized(Type t, object v)
        {
            if (typeof(PositionDataXml) == t)
            {
                PositionDataXml src = (PositionDataXml)v;
                _bar_count_exit = src.BarCountExit;
                _desc = src.Description;
                _is_pending = src.IsPending;
                _pending = src.PendingOrders;
                _pending_close = src.PendingClose;
                _pos_id = src.PosID;
                _pt = src.PositionType;
                _ptarget = src.ProfitTarget;
                _ptarget_price = src.ProfitTargetPrice;
                _ptarget_type = src.ProfitTargetType;
                _sloss = src.StopLoss;
                _sloss_price = src.StopLossPrice;
                _sloss_type = src.StopLossType;
                _sym = src.Symbol;
                _trades = src.Trades;
                _tstop = src.TrailingStop;
                _tstop_type = src.TrailingStopType;
            }
            return base.initFromSerialized(t, v);
        }
        */
    }

    [Serializable]
    public class PortfolioXml : XMLFileSerializeBase
    {
        List<PositionDataXml> _positions = new List<PositionDataXml>();
        public List<PositionDataXml> Positions { set { _positions = value; } get { return _positions; } }

        List<BrokerOrder> _pending = new List<BrokerOrder>();
        public List<BrokerOrder> PendingOrders { set { _pending = value; } get { return _pending; } }

        List<BrokerPosition> _broker_positions = new List<BrokerPosition>();
        public List<BrokerPosition> BrokerPositions { set { _broker_positions = value; } get { return _broker_positions; } }

        public override FunctionResult initFromSerialized<T>(T v)
        {
            if (typeof(T) == typeof(PortfolioXml))
            {
                PortfolioXml src = (PortfolioXml)((object)v);
                _broker_positions = src.BrokerPositions;
                _pending = src.PendingOrders;
                _positions = src.Positions;
            }
            return base.initFromSerialized<T>(v);
        }
        public override FunctionResult initFromSerialized(Type t, object v)
        {
            if (typeof(PortfolioXml) == t)
            {
                PortfolioXml src = (PortfolioXml)v;
                _broker_positions = src.BrokerPositions;
                _pending = src.PendingOrders;
                _positions = src.Positions;
            }
            return base.initFromSerialized(t, v);
        }
    }
    #endregion

    public interface IPluginLog
    {
        string FileName { set; get; }

        bool LogExceptions { set; get; }
        bool LogErrors { set; get; }
        bool LogDebug { set; get; }
        bool ShowErrors { set; get; }
    }

    public class PluginLog : IPluginLog
    {
        public PluginLog() { }
        public PluginLog(PluginLog src) { Copy(src); }

        ~PluginLog() { closeLog(); }

        public void Copy(PluginLog src)
        {
            _log_debug = src._log_debug;
            _log_errors = src._log_errors;
            _log_exceptions = src._log_exceptions;
            _fname = src._fname;
            _show_errors = src._show_errors;
            _monitor_timeout = src._monitor_timeout;
        }

        private bool _log_exceptions = true;
        public bool LogExceptions { set { _log_exceptions = value; } get { return (_log_exceptions); } }

        private bool _log_errors = true;
        public bool LogErrors { set { _log_errors = value; } get { return (_log_errors); } }

        private bool _log_debug = true;
        public bool LogDebug { set { _log_debug = value; } get { return (_log_debug); } }

        private bool _show_errors = true;
        public bool ShowErrors { set { _show_errors = value; } get { return (_show_errors); } }

        public bool IsOpen { get { return (_fs != null); } }

        private string _fname = null;
        public string FileName { get { return (_fname); } set { closeLog(); _fname = value; } }

        private FileStream _fs = null;
        private static object _lock = new object();

        private int _monitor_timeout = 1000;

        public void closeLog()
        {
            if (!Monitor.TryEnter(_lock, _monitor_timeout))
            {
                throw new OAPluginException("Unable to acquire lock on log file stream.");
            }
            try
            {
                if (_fs == null) { return; }

                _fs.Close();
                _fs = null;
            }
            finally { Monitor.Pulse(_lock); Monitor.Exit(_lock); }
        }
        public void openLog()
        { openLog(true); }

        private void openLog(bool do_lock)
        {
            if (do_lock && !Monitor.TryEnter(_lock, _monitor_timeout))
            {
                throw new OAPluginException("Unable to acquire lock on log file stream.");
            }
            try
            {
                if (_fs != null) { return; }
                _fs = new FileStream(_fname, FileMode.Append, FileAccess.Write);
            }
            finally { if (do_lock) { Monitor.Pulse(_lock); Monitor.Exit(_lock); } }
        }
        protected void writeMessage(string message)
        {
            if (!Monitor.TryEnter(_lock, _monitor_timeout))
            {
                throw new OAPluginException("Unable to acquire lock on log file stream.");
            }

            try
            {
                if (!IsOpen) { openLog(false); }
                string msg = DateTime.Now.ToString() + " [" + Thread.CurrentThread.ManagedThreadId + "] : " + message + "\n";
                byte[] msg_bytes = new UTF8Encoding(true).GetBytes(msg);

                _fs.Write(msg_bytes, 0, msg_bytes.Length);
                _fs.Flush();
            }
            catch (Exception e)
            {
                throw new OAPluginException("", e);
            }
            finally { Monitor.Pulse(_lock); Monitor.Exit(_lock); }
        }

        public void captureException(Exception e)
        {
            if (!_log_exceptions) { return; }
            string m = "";
            bool is_inner = false;
            for (Exception oe = e; oe != null; oe = oe.InnerException)
            {
                if (is_inner)
                { m += "\tInnerException:\n" + "\t" + oe.TargetSite + "\n\t" + oe.Message + "\n" + oe.StackTrace + "\n"; }
                else
                { m += "Exception:\n" + oe.TargetSite + "\n" + oe.Message + "\n" + oe.StackTrace + "\n"; }
                if (!is_inner) { is_inner = true; }
            }
            writeMessage(m);
        }
        public void captureDebug(string m)
        {
            if (!_log_debug) { return; }
            writeMessage(m);
        }

        public void captureError(string message, string title)
        {
            if (_log_errors) { writeMessage("ERROR : " + message); }
            if (_show_errors)
            {
                MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    public class BrokerLog : PluginLog
    {
        private bool _log_oa_out = true;
        public bool LogSendOA { set { _log_oa_out = value; } get { return (_log_oa_out); } }

        private bool _log_oa_in = true;
        public bool LogReceiveOA { set { _log_oa_in = value; } get { return (_log_oa_in); } }

        private bool _log_re_out = true;
        public bool LogSendRE { set { _log_re_out = value; } get { return (_log_re_out); } }

        private bool _log_re_in = true;
        public bool LogReceiveRE { set { _log_re_in = value; } get { return (_log_re_in); } }

        private bool _log_no_match = true;
        public bool LogUnknownEvents { set { _log_no_match = value; } get { return (_log_no_match); } }

        public void captureREIn(BrokerOrder order)
        {
            if (!_log_re_in) { return; }
            writeMessage("  RECEIVE RE ORDER : OrderID='" + order.OrderId + "' PosID='" + order.PositionID + "' Symbol='" + order.OrderSymbol.Name + "' Shares='" + order.Shares + "' Transaction='" + order.TransactionType + "' Type='" + order.OrderType + "' State='" + order.OrderState + "'.");
        }
        public void captureREIn(string s)
        {
            if (!_log_re_in) { return; }
            writeMessage("  RECEIVE RE ORDER : " + s);
        }
        public void captureREOut(BrokerOrder order, Fill fill, string s)
        {
            if (!_log_re_out) { return; }

            writeMessage(s);
            writeMessage("  SEND RE ORDER : OrderID='" + order.OrderId + "' PosID='" + order.PositionID + "' Symbol='" + order.OrderSymbol.Name + "' Shares='" + order.Shares + "' Transaction='" + order.TransactionType + "' Type='" + order.OrderType + "' State='" + order.OrderState + "'.");

            string n = "";
            if (fill != null)
            { n = "  SEND RE FILL  : " + fill.FillDateTime + " Qty='" + fill.Quantity + "' AccountPrice='" + fill.Price.AccountPrice + "' SymbolPrice='" + fill.Price.SymbolPrice + "'."; }
            else
            { n = "  SEND RE FILL  : (null)"; }
            writeMessage(n);


        }

        public void captureOAIn(Transaction trans, int act_id)
        {
            if (!_log_oa_in) { return; }
            writeTransaction("  RECEIVE OA EVENT", trans, act_id);
        }
        public void captureOAOut(Account acct, LimitOrder lo, string s)
        {
            if (!_log_oa_out) { return; }
            writeMessage("  SEND OA " + s + " LIMIT : Id='" + lo.Id + "' account='" + acct.AccountId + "' pair='" + lo.Pair + "' units='" + lo.Units + "' price='" + lo.Price + "'");
        }
        public void captureOAOut(Account acct, MarketOrder mo, string s)
        {
            if (!_log_oa_out) { return; }
            writeMessage("  SEND OA " + s + " MARKET : Id='" + mo.Id + "' account='" + acct.AccountId + "' pair='" + mo.Pair + "' units='" + mo.Units + "'");
        }

        public void captureUnknownEvent(Transaction trans, int act_id)
        {
            if (!_log_no_match) { return; }
            writeTransaction("  NOMATCH", trans, act_id);
        }

        public void writeTransaction(string s, Transaction trans, int act_id)
        {
            writeMessage(s + " : " + trans.Timestamp + " {" + act_id + ":" + trans.Base + "/" + trans.Quote + "} " + trans.Description + " [id='" + trans.TransactionNumber + "' link='" + trans.Link + "'].");
        }
    }

    public class TickLog : PluginLog
    {
        private bool _log_ticks = true;
        public bool LogTicks { set { _log_ticks = value; } get { return (_log_ticks); } }

        public void captureTick(Symbol sym, TickData tick)
        {
            if (!_log_ticks) { return; }
            writeMessage("TICK : symbol='" + sym.Name + "' time='" + tick.time + "' price='" + tick.price + "' type='" + tick.tickType + "' size='" + tick.size + "'");
        }
    }

    public enum DataFilterType { None, WeekendTimeFrame, PriceActivity };

    public class NewFilePickUITypeEditor : UITypeEditor
    {
        NewFilePickUITypeEditor() : base() { }
        public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext context)
        {
            return UITypeEditorEditStyle.Modal;
        }
        public override object EditValue(ITypeDescriptorContext context, IServiceProvider provider, object value)
        {
            OpenFileDialog form = new OpenFileDialog();
            
            form.FileName = (string)value;
            form.Filter = "data files (*.xml;*.csv)|*.xml;*.csv|log files (*.log)|*.log|all files (*.*)|*.*";
            
            if (form.FileName.EndsWith(".xml")) { form.FilterIndex = 1; }
            else if (form.FileName.EndsWith(".csv")) { form.FilterIndex = 1; }
            else if (form.FileName.EndsWith(".log")) { form.FilterIndex = 2; }
            else { form.FilterIndex = 3; }
            
            form.CheckFileExists = false;
            form.CheckPathExists = true;
            
            DialogResult fres = form.ShowDialog();
            if (fres != DialogResult.OK) { return value; }

            if (form.FileName == (string)value){ return value; }
            else{ return form.FileName; }
        }

    }

    public class PluginLogOptionsEditor : UITypeEditor
    {
        public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext context)
        {
            return UITypeEditorEditStyle.Modal;
        }
        public override object EditValue(ITypeDescriptorContext context, IServiceProvider provider, object value)
        {
            if (provider == null) { return value; }
            IWindowsFormsEditorService edSvc = (IWindowsFormsEditorService)provider.GetService(typeof(IWindowsFormsEditorService));
            if (edSvc == null) { return value; }

            PluginLogOptionsForm form = new PluginLogOptionsForm(new OAPluginLogOptions((OAPluginLogOptions)value));
            DialogResult fres = edSvc.ShowDialog(form);
            if (fres != DialogResult.OK) { return value; }

            OAPluginLogOptions vpl = (OAPluginLogOptions)value;
            OAPluginLogOptions editpl = form.LogOptions;
            //compare editpl and form.log, if no changes return value
            if (vpl.LogDebug == editpl.LogDebug &&
                vpl.LogErrors == editpl.LogErrors &&
                vpl.LogExceptions == editpl.LogExceptions &&
                vpl.ShowErrors == editpl.ShowErrors &&
                vpl.LogFileName == editpl.LogFileName)
            { return value; }
            else// value changed
            { return editpl; }
        }
    }
    
    [Serializable]
    [Editor(typeof(PluginLogOptionsEditor), typeof(UITypeEditor))]
    [TypeConverter(typeof(NamedConverter))]
    public class OAPluginLogOptions : INamedObject
    {
        public OAPluginLogOptions() { }
        public OAPluginLogOptions(string fn) { _log_fname = fn; }
        public OAPluginLogOptions(OAPluginLogOptions src) { Copy(src); }

        public void Copy(OAPluginLogOptions src)
        {
            OAPluginLogOptions rsrc = src;
            if (src == null) { rsrc = new OAPluginLogOptions(); }

            _log_debug = rsrc._log_debug;
            _log_errors = rsrc._log_errors;
            _log_exceptions = rsrc._log_exceptions;
            _log_fname = rsrc._log_fname;
            _show_errors = rsrc._show_errors;
        }
        private string _log_fname = "C:\\log.log";
        [Description("Set this to the desired log file name."), Editor(typeof(NewFilePickUITypeEditor), typeof(UITypeEditor))]
        public string LogFileName { set { _log_fname = value; } get { return (_log_fname); } }
        public string Name { get { return (_log_fname); } }
        public void SetName(string n) { _log_fname = n; }

        private bool _log_errors = true;
        [Description("Set this to true to enable logging of errors.")]
        public bool LogErrors { set { _log_errors = value; } get { return (_log_errors); } }

        private bool _show_errors = false;
        [Description("Set this to true to enable a message box dialog on errors.")]
        public bool ShowErrors { set { _show_errors = value; } get { return (_show_errors); } }

        private bool _log_exceptions = true;
        [Description("Set this to true to enable logging of exception details.")]
        public bool LogExceptions { set { _log_exceptions = value; } get { return (_log_exceptions); } }

        private bool _log_debug = true;
        [Description("Set this to true to enable debug messages.")]
        public bool LogDebug { set { _log_debug = value; } get { return (_log_debug); } }
    }

    [Serializable]
    public class OAPluginOptions
    {
        public OAPluginOptions() { setVersion(); }
        public OAPluginOptions(OAPluginOptions src) { setVersion(); Copy(src); }

        private void setVersion()
        {
            System.Reflection.Assembly a=System.Reflection.Assembly.GetAssembly(typeof(OAPluginOptions));
            if (a == null) { _version = "error"; return; }
            System.Reflection.AssemblyName an = a.GetName();
            if (an == null || an.Version==null) { _version = "error"; return; }
            
            _version = an.Version.ToString();
        }
        public void Copy(OAPluginOptions src)
        {
            OAPluginOptions rsrc = src;
            if (src == null) { rsrc = new OAPluginOptions(); }
            _opt_fname = rsrc._opt_fname;

            _tick_log_opt.Copy(rsrc._tick_log_opt);
            _hist_log_opt.Copy(rsrc._hist_log_opt);
            _broker_log_opt.Copy(rsrc._broker_log_opt);

            _log_trade_errors = rsrc._log_trade_errors;

            _log_re_in = rsrc._log_re_in;
            _log_re_out = rsrc._log_re_out;
            _log_oa_in = rsrc._log_oa_in;
            _log_oa_out = rsrc._log_oa_out;
            _log_unknown_events = rsrc._log_unknown_events;

            _order_log_fname = rsrc._order_log_fname;

            _log_fxclient = rsrc._log_fxclient;
            _fxclient_log_fname = rsrc._fxclient_log_fname;

            _log_ticks = rsrc._log_ticks;

            _use_game = rsrc._use_game;

            _data_filter_type = rsrc._data_filter_type;
            _weekend_end_day = rsrc._weekend_end_day;
            _weekend_end_time = rsrc._weekend_end_time;
            _weekend_start_day = rsrc._weekend_start_day;
            _weekend_start_time = rsrc._weekend_start_time;

            _trade_entity_fname = rsrc._trade_entity_fname;
            _default_account = rsrc._default_account;
            //_default_base_spread = rsrc._default_base_spread;
            _default_lower_bound = rsrc._default_lower_bound;
            _default_lower_type = rsrc._default_lower_type;
            _default_upper_bound = rsrc._default_upper_bound;
            _default_upper_type = rsrc._default_upper_type;

            _watchdog_restart_attempt_threshold = rsrc._watchdog_restart_attempt_threshold;
            _watchdog_min_time_to_sleep = rsrc._watchdog_min_time_to_sleep;
            _watchdog_max_time_to_sleep = rsrc._watchdog_max_time_to_sleep;
        }

        #region RightEdge 'serialization'
        public bool loadRESettings(RightEdge.Common.SerializableDictionary<string, string> settings)
        {
            try
            {
                if (settings.ContainsKey("Broker.LogFileName")) { _broker_log_opt.LogFileName = settings["Broker.LogFileName"]; }
                if (settings.ContainsKey("Broker.LogErrorsEnabled")) { _broker_log_opt.LogErrors = bool.Parse(settings["Broker.LogErrorsEnabled"]); }
                if (settings.ContainsKey("Broker.LogExceptionsEnabled")) { _broker_log_opt.LogExceptions = bool.Parse(settings["Broker.LogExceptionsEnabled"]); }
                if (settings.ContainsKey("Broker.LogDebugEnabled")) { _broker_log_opt.LogDebug = bool.Parse(settings["Broker.LogDebugEnabled"]); }
                if (settings.ContainsKey("Broker.ShowErrorsEnabled")) { _broker_log_opt.ShowErrors = bool.Parse(settings["Broker.ShowErrorsEnabled"]); }

                if (settings.ContainsKey("Tick.LogFileName")) { _tick_log_opt.LogFileName = settings["Tick.LogFileName"]; }
                if (settings.ContainsKey("Tick.LogErrorsEnabled")) { _tick_log_opt.LogErrors = bool.Parse(settings["Tick.LogErrorsEnabled"]); }
                if (settings.ContainsKey("Tick.LogExceptionsEnabled")) { _tick_log_opt.LogExceptions = bool.Parse(settings["Tick.LogExceptionsEnabled"]); }
                if (settings.ContainsKey("Tick.LogDebugEnabled")) { _tick_log_opt.LogDebug = bool.Parse(settings["Tick.LogDebugEnabled"]); }
                if (settings.ContainsKey("Tick.ShowErrorsEnabled")) { _tick_log_opt.ShowErrors = bool.Parse(settings["Tick.ShowErrorsEnabled"]); }

                if (settings.ContainsKey("History.LogFileName")) { _hist_log_opt.LogFileName = settings["History.LogFileName"]; }
                if (settings.ContainsKey("History.LogErrorsEnabled")) { _hist_log_opt.LogErrors = bool.Parse(settings["History.LogErrorsEnabled"]); }
                if (settings.ContainsKey("History.LogExceptionsEnabled")) { _hist_log_opt.LogExceptions = bool.Parse(settings["History.LogExceptionsEnabled"]); }
                if (settings.ContainsKey("History.LogDebugEnabled")) { _hist_log_opt.LogDebug = bool.Parse(settings["History.LogDebugEnabled"]); }
                if (settings.ContainsKey("History.ShowErrorsEnabled")) { _hist_log_opt.ShowErrors = bool.Parse(settings["History.ShowErrorsEnabled"]); }

                if (settings.ContainsKey("OrderLogFileName")) { _order_log_fname = settings["OrderLogFileName"]; }

                if (settings.ContainsKey("FXClientLogFileName")) { _fxclient_log_fname = settings["FXClientLogFileName"]; }
                if (settings.ContainsKey("LogFXClientEnabled")) { _log_fxclient = bool.Parse(settings["LogFXClientEnabled"]); }

                if (settings.ContainsKey("LogTicksEnabled")) { _log_ticks = bool.Parse(settings["LogTicksEnabled"]); }

                if (settings.ContainsKey("GameServerEnabled")) { _use_game = bool.Parse(settings["GameServerEnabled"]); }

                if (settings.ContainsKey("LogTradeErrorsEnabled")) { _log_trade_errors = bool.Parse(settings["LogTradeErrorsEnabled"]); }

                if (settings.ContainsKey("LogOandaSend")) { _log_oa_out = bool.Parse(settings["LogOandaSend"]); }
                if (settings.ContainsKey("LogOandaReceive")) { _log_oa_in = bool.Parse(settings["LogOandaReceive"]); }
                if (settings.ContainsKey("LogRightEdgeSend")) { _log_re_out = bool.Parse(settings["LogRightEdgeSend"]); }
                if (settings.ContainsKey("LogRightEdgeReceive")) { _log_re_in = bool.Parse(settings["LogRightEdgeReceive"]); }
                if (settings.ContainsKey("LogUnknownEventsEnabled")) { _log_unknown_events = bool.Parse(settings["LogUnknownEventsEnabled"]); }


                if (settings.ContainsKey("DataFilterType")) { _data_filter_type = (DataFilterType)Enum.Parse(typeof(DataFilterType), settings["DataFilterType"]); }
                if (settings.ContainsKey("WeekendStartDay")) { _weekend_start_day = (DayOfWeek)Enum.Parse(typeof(DayOfWeek), settings["WeekendStartDay"]); }
                if (settings.ContainsKey("WeekendEndDay")) { _weekend_end_day = (DayOfWeek)Enum.Parse(typeof(DayOfWeek), settings["WeekendEndDay"]); }
                if (settings.ContainsKey("WeekendStartTime")) { _weekend_start_time = TimeSpan.Parse(settings["WeekendStartTime"]); }
                if (settings.ContainsKey("WeekendEndTime")) { _weekend_end_time = TimeSpan.Parse(settings["WeekendEndTime"]); }

                if (settings.ContainsKey("AccountValuesFileName")) { _account_values_fname = settings["AccountValuesFileName"]; }
                if (settings.ContainsKey("AccountValuesEnabled")) { _use_account_values = bool.Parse(settings["AccountValuesEnabled"]); }

                if (settings.ContainsKey("TradeEntityFileName")) { _trade_entity_fname = settings["TradeEntityFileName"]; }
                if (settings.ContainsKey("TradeEntityName")) { _trade_entity_name = settings["TradeEntityName"]; }
                if (settings.ContainsKey("DefaultAccount")) { _default_account = settings["DefaultAccount"]; }
                
                //if (settings.ContainsKey("DefaultOrderSize")) { _default_order_size = double.Parse(settings["DefaultOrderSize"]); }
                //if (settings.ContainsKey("DefaultOrderSizeType")) { _default_order_size_type = (ValueScaleType)Enum.Parse(typeof(ValueScaleType), settings["DefaultOrderSizeType"]); }
                //if (settings.ContainsKey("DefaultBaseSpread")) { _default_base_spread = settings["DefaultBaseSpread"]; }
                
                if (settings.ContainsKey("DefaultLowerBound")) { _default_lower_bound = double.Parse(settings["DefaultLowerBound"]); }
                if (settings.ContainsKey("DefaultLowerType")) { _default_lower_type = (ValueScaleType)Enum.Parse(typeof(ValueScaleType), settings["DefaultLowerType"]); }
                if (settings.ContainsKey("DefaultUpperBound")) { _default_upper_bound = double.Parse(settings["DefaultUpperBound"]); }
                if (settings.ContainsKey("DefaultUpperType")) { _default_upper_type = (ValueScaleType)Enum.Parse(typeof(ValueScaleType), settings["DefaultUpperType"]); }

                if (settings.ContainsKey("WatchdogRestartAttemptThreshold")) { _watchdog_restart_attempt_threshold = Int32.Parse(settings["WatchdogRestartAttemptThreshold"]); }
                if (settings.ContainsKey("WatchdogMinimumSleepTime")) { _watchdog_min_time_to_sleep = Int32.Parse(settings["WatchdogMinimumSleepTime"]); }
                if (settings.ContainsKey("WatchdogMaximumSleepTime")) { _watchdog_max_time_to_sleep = Int32.Parse(settings["WatchdogMaximumSleepTime"]); }
            }
            catch (Exception e)
            {//settings parse/load problem....
                throw new OAPluginException("Unable to load options object from RE Settings dictionary. " + e.Message, e);
            }
            return true;
        }
        public bool saveRESettings(ref RightEdge.Common.SerializableDictionary<string, string> settings)
        {
            settings["Broker.LogFileName"] = _broker_log_opt.LogFileName;
            settings["Broker.LogErrorsEnabled"] = _broker_log_opt.LogErrors.ToString();
            settings["Broker.LogExceptionsEnabled"] = _broker_log_opt.LogExceptions.ToString();
            settings["Broker.LogDebugEnabled"] = _broker_log_opt.LogDebug.ToString();
            settings["Broker.ShowErrorsEnabled"] = _broker_log_opt.ShowErrors.ToString();

            settings["Tick.LogFileName"] = _tick_log_opt.LogFileName;
            settings["Tick.LogErrorsEnabled"] = _tick_log_opt.LogErrors.ToString();
            settings["Tick.LogExceptionsEnabled"] = _tick_log_opt.LogExceptions.ToString();
            settings["Tick.LogDebugEnabled"] = _tick_log_opt.LogDebug.ToString();
            settings["Tick.ShowErrorsEnabled"] = _tick_log_opt.ShowErrors.ToString();

            settings["History.LogFileName"] = _hist_log_opt.LogFileName;
            settings["History.LogErrorsEnabled"] = _hist_log_opt.LogErrors.ToString();
            settings["History.LogExceptionsEnabled"] = _hist_log_opt.LogExceptions.ToString();
            settings["History.LogDebugEnabled"] = _hist_log_opt.LogDebug.ToString();
            settings["History.ShowErrorsEnabled"] = _hist_log_opt.ShowErrors.ToString();

            settings["OrderLogFileName"] = _order_log_fname;

            settings["FXClientLogFileName"] = _fxclient_log_fname;
            settings["LogFXClientEnabled"] = _log_fxclient.ToString();

            settings["LogTicksEnabled"] = _log_ticks.ToString();

            settings["GameServerEnabled"] = _use_game.ToString();

            settings["LogTradeErrorsEnabled"] = _log_trade_errors.ToString();

            settings["LogOandaSend"] = _log_oa_out.ToString();
            settings["LogOandaReceive"] = _log_oa_in.ToString();
            settings["LogRightEdgeSend"] = _log_re_out.ToString();
            settings["LogRightEdgeReceive"] = _log_re_in.ToString();
            settings["LogUnknownEventsEnabled"] = _log_unknown_events.ToString();

            settings["DataFilterType"] = _data_filter_type.ToString();
            settings["WeekendStartDay"] = _weekend_start_day.ToString();
            settings["WeekendEndDay"] = _weekend_end_day.ToString();
            settings["WeekendStartTime"] = _weekend_start_time.ToString();
            settings["WeekendEndTime"] = _weekend_end_time.ToString();

            settings["AccountValuesFileName"] = _account_values_fname;
            settings["AccountValuesEnabled"] = _use_account_values.ToString();

            settings["TradeEntityFileName"] = _trade_entity_fname;
            settings["TradeEntityName"] = _trade_entity_name;

            settings["DefaultAccount"] = _default_account;
            //settings["DefaultOrderSize"] = _default_order_size.ToString();
            //settings["DefaultOrderSizeType"] = _default_order_size_type.ToString();
            //settings["DefaultBaseSpread"] = _default_base_spread.ToString();
            settings["DefaultLowerBound"] = _default_lower_bound.ToString();
            settings["DefaultLowerType"] = _default_lower_type.ToString();
            settings["DefaultUpperBound"] = _default_upper_bound.ToString();
            settings["DefaultUpperType"] = _default_upper_type.ToString();

            settings["WatchdogRestartAttemptThreshold"] = _watchdog_restart_attempt_threshold.ToString();
            settings["WatchdogMinimumSleepTime"] = _watchdog_min_time_to_sleep.ToString();
            settings["WatchdogMaximumSleepTime"] = _watchdog_max_time_to_sleep.ToString();

            return true;
        }
        #endregion

        #region XML Serialization
        private string _opt_fname = "";
        [XmlIgnore, Browsable(false)]
        public string OptionsFileName { set { _opt_fname = value; } get { return (_opt_fname); } }

        public void saveSettings()
        {
            XmlSerializer mySerializer = new XmlSerializer(typeof(OAPluginOptions));
            StreamWriter myWriter = new StreamWriter(_opt_fname);
            mySerializer.Serialize(myWriter, this);
            myWriter.Close();
            myWriter.Dispose();
        }

        public static OAPluginOptions loadSettings(string opt_fname)
        {
            XmlSerializer mySerializer = new XmlSerializer(typeof(OAPluginOptions));
            OAPluginOptions opts;
            FileStream myFileStream = null;
            try
            {
                myFileStream = new FileStream(opt_fname, FileMode.Open);
            }
            catch (System.IO.IOException)
            {
                opts = new OAPluginOptions();
                opts._opt_fname = opt_fname;
                return (opts);
            }
            opts = (OAPluginOptions)mySerializer.Deserialize(myFileStream);
            myFileStream.Close();
            myFileStream.Dispose();
            opts._opt_fname = opt_fname;
            return (opts);
        }
        #endregion

        private string _version;
        [XmlIgnore(),ReadOnly(true),Description("The Oanda plugin version."), Category("Version")]
        public string Version { set { _version = value; } get { return (_version); } }


        #region trade entity options
        #region default value members
        private string _default_account = string.Empty;
        [XmlIgnore, Description("If the trade entities file is not found, then this is the default account value."), Category("Trade Values")]
        public string DefaultAccount { get { return _default_account; } set { _default_account = value; } }

        //double _default_base_spread = 0.0;
        //[XmlIgnore, Description(""), Category("Trade Values")]
        //public double DefaultBaseSpread { get { return _default_base_spread; } set { _default_base_spread = value; } }

        //ValueScaleType _default_order_size_type = ValueScaleType.Unset;
        //[XmlIgnore, Description(""), Category("Trade Values")]
        //public ValueScaleType DefaultOrderSizeType { get { return _default_order_size_type; } set { _default_order_size_type = value; } }
        //double _default_order_size = 0.0;
        //[XmlIgnore, Description(""), Category("Trade Values")]
        //public double DefaultOrderSize { get { return _default_order_size; } set { _default_order_size = value; } }

        ValueScaleType _default_upper_type = ValueScaleType.Unset;
        [XmlIgnore, Description("If the trade entities file is not found, then this is the default upper bounds value type."), Category("Trade Values")]
        public ValueScaleType DefaultUpperBoundsType { get { return _default_upper_type; } set { _default_upper_type = value; } }
        double _default_upper_bound = 0.0;
        [XmlIgnore, Description("If the trade entities file is not found, then this is the default upper bounds value."), Category("Trade Values")]
        public double DefaultUpperBoundsValue { get { return _default_upper_bound; } set { _default_upper_bound = value; } }

        ValueScaleType _default_lower_type = ValueScaleType.Unset;
        [XmlIgnore, Description("If the trade entities file is not found, then this is the default lower bounds value type."), Category("Trade Values")]
        public ValueScaleType DefaultLowerBoundsType { get { return _default_lower_type; } set { _default_lower_type = value; } }
        double _default_lower_bound = 0.0;
        [XmlIgnore, Description("If the trade entities file is not found, then this is the default lower bounds value."), Category("Trade Values")]
        public double DefaultLowerBoundsValue { get { return _default_lower_bound; } set { _default_lower_bound = value; } }
        #endregion        //opt filename

        private string _trade_entity_name = "broker";
        [Description("The Entity Name to use for the broker plugin."), Category("Trade Values")]
        public string TradeEntityName { set { _trade_entity_name = value; } get { return (_trade_entity_name); } }

        private string _trade_entity_fname = string.Empty;
        [Description("The Trade Entities file name. If specified, this file contains the default and specific trading values that will be used."), Category("Trade Values"), Editor(typeof(NewFilePickUITypeEditor), typeof(UITypeEditor))]
        public string TradeEntityFileName { set { _trade_entity_fname = value; } get { return (_trade_entity_fname); } }

        #endregion
        
        #region account values data file options
        private bool _use_account_values = true;
        [Description("If enabled, the broker will write all relevant account values for all accounts to the data file when an account value request is received from RightEdge."), Category("Account Values"), Editor(typeof(NewFilePickUITypeEditor), typeof(UITypeEditor))]
        public bool AccountValuesEnabled { get { return _use_account_values; } set { _use_account_values = value; } }

        private string _account_values_fname = string.Empty;
        [Description("The file name for the account values data."), Category("Account Values"), Editor(typeof(NewFilePickUITypeEditor), typeof(UITypeEditor))]
        public string AccountValuesFileName { set { _account_values_fname = value; } get { return (_account_values_fname); } }
        #endregion

        #region fxclient options
        private string _fxclient_log_fname = "C:\\fxclient.log";
        [Description("Set this to the file name for the internal fxClientAPI logging."), Category("Logging, FXClient"), Editor(typeof(NewFilePickUITypeEditor), typeof(UITypeEditor))]
        public string FXClientLogFileName { set { _fxclient_log_fname = value; } get { return (_fxclient_log_fname); } }

        private bool _log_fxclient = false;
        [Description("Enable this for the raw internal fxClientAPI log. WARNING : this is a HUGE FILE and will contain your PASSWORD IN PLAIN TEXT!!"), Category("Logging, FXClient")]
        public bool LogFXClientEnabled { set { _log_fxclient = value; } get { return (_log_fxclient); } }
        #endregion

        private OAPluginLogOptions _broker_log_opt = new OAPluginLogOptions("c:\\broker.log");
        [Description("Logging options for the broker component."), Category("Logging, Broker")]
        public OAPluginLogOptions LogOptionsBroker { set { _broker_log_opt = value; } get { return (_broker_log_opt); } }

        #region broker options
        private bool _use_game = true;
        [Description("Set this to true for fxGame, if false fxTrade will be used."), Category("Broker Options")]
        public bool GameServerEnabled { set { _use_game = value; } get { return (_use_game); } }

        private string _order_log_fname = "C:\\orders.xml";
        [Description("Set this to the file name for storing order information."), Editor(typeof(FilePickUITypeEditor), typeof(UITypeEditor)), Category("Broker Options")]
        public string OrderLogFileName { set { _order_log_fname = value; } get { return (_order_log_fname); } }

        private bool _log_trade_errors = true;
        [Description("Set this to true to enable logging of all order submission errors."), Category("Logging, Broker")]
        public bool LogTradeErrors { set { _log_trade_errors = value; } get { return (_log_trade_errors); } }

        #region broker events
        private bool _log_oa_in = true;
        [Description("Set this to true to enable logging of Oanda Account Event Responses."), Category("Logging, Broker Events")]
        public bool LogOandaReceive { set { _log_oa_in = value; } get { return (_log_oa_in); } }

        private bool _log_oa_out = true;
        [Description("Set this to true to enable logging of Oanda Account Actions."), Category("Logging, Broker Events")]
        public bool LogOandaSend { set { _log_oa_out = value; } get { return (_log_oa_out); } }

        private bool _log_re_in = true;
        [Description("Set this to true to enable logging of RightEdge broker orderbook calls."), Category("Logging, Broker Events")]
        public bool LogRightEdgeReceive { set { _log_re_in = value; } get { return (_log_re_in); } }

        private bool _log_re_out = true;
        [Description("Set this to true to enable logging of RightEdge OrderUpdated calls."), Category("Logging, Broker Events")]
        public bool LogRightEdgeSend { set { _log_re_out = value; } get { return (_log_re_out); } }

        private bool _log_unknown_events = true;
        [Description("Set this to true to enable logging of event details for unknown Oanda Account Responses."), Category("Logging, Broker Events")]
        public bool LogUnknownEvents { set { _log_unknown_events = value; } get { return (_log_unknown_events); } }
        #endregion
        #endregion

        private OAPluginLogOptions _tick_log_opt = new OAPluginLogOptions("c:\\tick.log");
        [Description("Logging options for the live data component."), Category("Logging, Tick")]
        public OAPluginLogOptions LogOptionsTick { set { _tick_log_opt = value; } get { return (_tick_log_opt); } }

        #region tick logging options
        private bool _log_ticks = false;
        [Description("Set this to true to enable logging of tick data to the tick log."), Category("Logging, Tick")]
        public bool LogTicks { set { _log_ticks = value; } get { return (_log_ticks); } }
        #endregion

        private OAPluginLogOptions _hist_log_opt = new OAPluginLogOptions("c:\\history.log");
        [Description("Logging options for the historic data component."), Category("Logging, History")]
        public OAPluginLogOptions LogOptionsHistory { set { _hist_log_opt = value; } get { return (_hist_log_opt); } }

        #region history filtering options
        private DataFilterType _data_filter_type = DataFilterType.WeekendTimeFrame;
        [Description("There are 3 filtering options for historic data downloads. Set this to 'WeekendTimeFrame' to enable the filter using the specified Weekend date/time range. Set it to 'PriceActivity' to filter bars with no price movement. Set it to 'None' to disable all filtering."), Category("History Filter Options")]
        public DataFilterType DataFilterType { set { _data_filter_type = value; } get { return (_data_filter_type); } }

        private DayOfWeek _weekend_start_day = DayOfWeek.Friday;
        [Description("The day of the week the weekend data starts."), Category("History Filter Options")]
        public DayOfWeek WeekendStartDay { set { _weekend_start_day = value; } get { return (_weekend_start_day); } }
        private TimeSpan _weekend_start_time = new TimeSpan(17, 0, 0);
        [Description("The time of day the weekend data starts."), Category("History Filter Options")]
        public TimeSpan WeekendStartTime { set { _weekend_start_time = value; } get { return (_weekend_start_time); } }

        private DayOfWeek _weekend_end_day = DayOfWeek.Sunday;
        [Description("The day of the week the weekend data stops."), Category("History Filter Options")]
        public DayOfWeek WeekendEndDay { set { _weekend_end_day = value; } get { return (_weekend_end_day); } }
        private TimeSpan _weekend_end_time = new TimeSpan(11, 0, 0);
        [Description("The time of day the weekend data stops."), Category("History Filter Options")]
        public TimeSpan WeekendEndTime { set { _weekend_end_time = value; } get { return (_weekend_end_time); } }
        #endregion

        #region watchdog reconnect options
        private int _watchdog_restart_attempt_threshold = 3;
        [Description("The maximum number of re-connection attempts."), Category("Reconnect Options")]
        public int WatchdogMaxReconnectThreshold { get { return _watchdog_restart_attempt_threshold; } set { _watchdog_restart_attempt_threshold = value; } }
        private int _watchdog_min_time_to_sleep = 10;
        [Description("The minimum delay in seconds between re-connection attempts."), Category("Reconnect Options")]
        public int WatchdogMinSleepTime { get { return _watchdog_min_time_to_sleep; } set { _watchdog_min_time_to_sleep = value; } }
        private int _watchdog_max_time_to_sleep = 120;
        [Description("The maximum delay in seconds between re-connection attempts."), Category("Reconnect Options")]
        public int WatchdogMaxSleepTime { get { return _watchdog_max_time_to_sleep; } set { _watchdog_max_time_to_sleep = value; } }
        #endregion
    }

    #region Oanda fxEvent managers
    public class RateTicker : fxRateEvent
    {
        public RateTicker(Symbol sym, OandAPlugin parent) : base(new fxPair(sym.Name)) { _symbol = sym; _parent = parent; }

        private OandAPlugin _parent;

        private Symbol _symbol;
        public Symbol Symbol { get { return (_symbol); } }

        public int TickCount = 0;
        public double High = 0.0;
        public double Low = double.MaxValue;

        public override void handle(fxEventInfo ei, fxEventManager em)
        {
            TickCount++;
            _parent.handleRateTicker(this, (fxRateEventInfo)ei, em);
        }
    }

    public delegate void AccountResponseDelegate(Object sender, AccountResponseEventArgs args);
    public class AccountResponseEventArgs : EventArgs
    {
        public fxAccountEventInfo aei;
        public fxEventManager em;
    }

    public class AccountResponder : fxAccountEvent
    {
        public AccountResponder(int act_id, string base_currency, OandAPlugin p) : base() { _account_id = act_id; _base_currency = base_currency; _parent = p; if (_use_temp_log) { _temp_log = new PluginLog(); _temp_log.FileName = _log_file; } }

        private bool _use_temp_log = false;
        private string _log_file = "C:\\Storage\\src\\RE-LogFiles\\account_responder.log";
        private PluginLog _temp_log = null;

        private OandAPlugin _parent = null;

        private bool _active = true;
        public bool Active { get { return (_active); } set { _active = value; } }

        private int _account_id = 0;
        public int AccountID { get { return (_account_id); } }

        private string _base_currency = string.Empty;
        public string BaseCurrency { get { return (_base_currency); } }
        public override void exception_call_back()
        {
            if (_parent.BrokerLog != null)
            { _parent.BrokerLog.captureDebug("AccountResponder detected an exception_call_back()"); }
            base.exception_call_back();
        }
        public override bool match(fxEventInfo ei)
        {
            //if (ei is fxAccountEventInfo)
            //{ _parent.ResponseProcessor.HandleAccountResponder(this, (fxAccountEventInfo)ei, null); }
            return base.match(ei);
        }

        public override void handle(fxEventInfo ei, fxEventManager em)
        {
            if (_use_temp_log) { _temp_log.captureDebug("AccountResponder.handle() start..."); }
            if (ei is fxAccountEventInfo)
            {
                fxAccountEventInfo fxei = (fxAccountEventInfo)ei;
                if (_use_temp_log) { _temp_log.captureDebug("  calling responder for account event : desc='" + fxei.Transaction.Description + "' id='" + fxei.Transaction.TransactionNumber + "' link='" + fxei.Transaction.Link + "'"); }
                _parent.ResponseProcessor.HandleAccountResponder(this, fxei, em);
            }
            base.handle(ei, em);
            if (_use_temp_log) { _temp_log.captureDebug("AccountResponder.handle() stop..."); }
        }
    }
    #endregion


        
    public class fxClientWrapper
    {
        private OandAPlugin _parent;
        public fxClientWrapper(OandAPlugin p) { _parent = p; }

        //account listeners will connect to the _in client
        //in case the out channel disconnects due to an Oanda Exception
        //this way no account events will be lost...
        private fxClient _fx_client_in = null;

        //execution will happen over the _out channel
        private fxClient _fx_client_out = null;



        public bool IsInit { get { return (_fx_client_in != null); } }

        private const int _smallest_account_num = 10000;

        private OAPluginOptions _opts = null;
        [XmlIgnore]
        public OAPluginOptions OAPluginOptions { set { _opts = value; } get { return (_opts); } }

        private BrokerLog _broker_log = null;
        [XmlIgnore]
        public BrokerLog BrokerLog { set { _broker_log = value; } get { return (_broker_log); } }

        private PluginLog _log = null;
        [XmlIgnore]
        public PluginLog Log { set { _log = value; } get { return (_log); } }

        private string _user = string.Empty;
        private string _pw = string.Empty;
        private bool _is_restart = false;

        public FXClientResult Connect(ServiceConnectOptions connectOptions, string u, string pw)
        {
            if (!_is_restart)
            { _log.captureDebug("--------------- { RightEdge Oanda Plugin Version (" + _opts.Version + ") } ---------------"); }
            _log.captureDebug("Connect() called.");

            _user = u;
            _pw = pw;

            lock (_fx_client_in_lock)
            {
                FXClientResult res = connectIn(connectOptions,_is_restart);
                _is_restart = true;
                if (res.Error) { return res; }

                if (connectOptions == ServiceConnectOptions.Broker)
                { res = connectOut(); }

                return res;
            }
        }
        public FXClientResult Disconnect()
        {
            lock (_fx_client_in_lock)
            {
                FXClientResult res = new FXClientResult();
                if (_fx_client_in == null) { return res; }

                try
                {
                    if (_watchdog_thread != null) { stopDataWatchdog(); }

                    if (_fx_client_in.IsLoggedIn)
                    {
                        _fx_client_in.Logout();//FIX ME <--- this can hang indefinitely....
                    }
                    _fx_client_in.Destroy();
                    _fx_client_in = null;
                    return res;
                }
                catch (SessionException oase)
                {
                    _log.captureException(oase);
                    res.setError("session exception : " + oase.Message);
                    return res;
                }
                catch (OAException oae)
                {
                    _log.captureException(oae);
                    res.setError("Error disconnecting from oanda : '" + oae.Message + "'");
                    return res;
                }
                catch (Exception e)
                {
                    _log.captureException(e);
                    res.setError("Unhandled exception : " + e.Message);
                    return res;
                }
            }
        }

        private Thread _watchdog_thread = null;
        private int _watchdog_restart_attempt_count = 0;
        private int _watchdog_restart_complete_count = 0;
        private const int _watchdog_restart_complete_threshold = 15;
        


        private FXClientResult connectIn(ServiceConnectOptions connectOptions,bool is_restart)
        {
            FXClientResult res = new FXClientResult();

            if (connectOptions == ServiceConnectOptions.Broker)
            { _log.captureDebug("connectIn() called."); }

            // Note: callers should acquire the _fx_client_in_lock before calling this method
            if (_fx_client_in != null)
            {
                res.setError("Connect called on existing fxclient", FXClientResponseType.Rejected, true);
                return res;
            }

            bool wrt = false;
            bool wka = false;
            bool start_data_watchdog = false;
            switch (connectOptions)
            {
                case ServiceConnectOptions.Broker:
                    wka = true;
                    wrt = false;
                    break;
                case ServiceConnectOptions.LiveData:
                    start_data_watchdog = !is_restart;
                    wka = true;
                    wrt = true;
                    break;
                case ServiceConnectOptions.HistoricalData:
                    wrt = true;
                    break;
                default:
                    res.setError("Connect() received an unknown ServiceConnectOptions parameter value.", FXClientResponseType.Rejected, true);
                    return res;
            }

            if (_opts.GameServerEnabled)
            {
                _fx_client_in = new fxGame();
            }
            else
            {
                _fx_client_in = new fxTrade();

                res.setError("contact oanda when ready to turn on live", FXClientResponseType.Rejected, true);
                return res;
            }
            _log.captureDebug("Using fxClient version : (" + _fx_client_in.Version + ")");

            if (_opts.LogFXClientEnabled)
            {
                _fx_client_in.Logfile = _opts.FXClientLogFileName;
            }

            try
            {
                _fx_client_in.WithRateThread = wrt;
                _fx_client_in.WithKeepAliveThread = wka;
                _fx_client_in.Login(_user, _pw);

                if (start_data_watchdog) { startDataWatchdog(); }
                return res;
            }
            catch (SessionException oase)
            {
                _log.captureException(oase);
                res.setError("session exception : " + oase.Message, FXClientResponseType.Disconnected, true);
                return res;
            }
            catch (OAException oae)
            {
                _log.captureException(oae);
                res.setError("login failed : " + oae.Message, FXClientResponseType.Rejected, true);
                return res;
            }
            catch (Exception e)
            {
                _log.captureException(e);
                res.setError("Unhandled exception : " + e.Message, FXClientResponseType.Rejected, !_fx_client_in.IsLoggedIn);
                return res;
            }
        }
        private FXClientResult connectOut()
        {
            FXClientResult res = new FXClientResult();

            _log.captureDebug("connectOut() called.");

            if (_fx_client_out != null)
            {
                _fx_client_out.Destroy();
                _fx_client_out = null;
            }

            if (_opts.GameServerEnabled)
            {
                _fx_client_out = new fxGame();
            }
            else
            {
                _fx_client_out = new fxTrade();

                res.setError("contact oanda when ready to turn on live", FXClientResponseType.Rejected, true);
                return res;
            }

            if (_opts.LogFXClientEnabled)
            {
                _fx_client_out.Logfile = _opts.FXClientLogFileName;
            }

            try
            {
                _fx_client_out.WithRateThread = true;//need rate table to query rates for non-local price conversions
                _fx_client_out.WithKeepAliveThread = true;
                _fx_client_out.Login(_user, _pw);
                return res;
            }
            catch (SessionException oase)
            {
                _log.captureException(oase);
                res.setError("session exception : " + oase.Message, FXClientResponseType.Disconnected, true);
                return res;
            }
            catch (OAException oae)
            {
                _log.captureException(oae);
                res.setError("login failed : " + oae.Message, FXClientResponseType.Rejected, true);
                return res;
            }
            catch (Exception e)
            {
                _log.captureException(e);
                res.setError("Unhandled exception : " + e.Message, FXClientResponseType.Rejected, !_fx_client_out.IsLoggedIn);
                return res;
            }
        }


        public FXClientResult SetWatchedSymbols(List<RateTicker> rate_tickers, List<Symbol> symbols, OandAPlugin parent)
        {
            lock (_fx_client_in_lock)
            {
                FXClientResult res = new FXClientResult();
                fxEventManager em;
                try
                {
                    try
                    {
                        if (_fx_client_in == null || !_fx_client_in.IsLoggedIn)
                        {
                            res.setError("fxClient is not logged in.", FXClientResponseType.Disconnected, true);
                            return res;
                        }

                        em = _fx_client_in.RateTable.GetEventManager();

                        foreach (RateTicker oldrt in rate_tickers)
                        { em.remove(oldrt); }
                        rate_tickers.Clear();
                    }
                    catch (SessionException oase)
                    {
                        _log.captureException(oase);
                        res.setError("session exception : " + oase.Message, FXClientResponseType.Disconnected, true);
                        return res;
                    }
                    catch (OAException oae)
                    {
                        _log.captureException(oae);
                        res.setError("Unable to clear watched symbols : '" + oae.Message + "'", FXClientResponseType.Rejected, true);
                        return res;
                    }

                    foreach (Symbol sym in symbols)
                    {
                        RateTicker rt = new RateTicker(sym, parent);
                        rate_tickers.Add(rt);
                        try
                        {
                            em.add(rt);
                        }
                        catch (SessionException oase)
                        {
                            _log.captureException(oase);
                            res.setError("session exception : " + oase.Message, FXClientResponseType.Disconnected, true);
                            return res;
                        }
                        catch (OAException oae)
                        {
                            _log.captureException(oae);
                            res.setError("Unable to set watch on symbol '" + sym.ToString() + "' : '" + oae.Message + "'", FXClientResponseType.Rejected, true);
                            return res;
                        }
                    }
                    return res;
                }
                catch (Exception e)
                {
                    _log.captureException(e);
                    res.setError("Unhandled exception : " + e.Message);
                    return res;
                }
            }
        }
        
        private void startDataWatchdog()
        {
            _log.captureDebug("Starting up the Data Watchdog Thread...");
            _watchdog_thread = new Thread(new ThreadStart(watchdogMain));
            _watchdog_thread.Name = "LiveDataWatchdog";
            _watchdog_thread.IsBackground = true;
            _watchdog_thread.Start();
        }

        private readonly object _fx_client_in_lock = new object();

        private void watchdogMain()
        {
            try
            {
                do
                {
                    int time_to_sleep = _opts.WatchdogMinSleepTime + (_watchdog_restart_attempt_count * _opts.WatchdogMinSleepTime);
                    if (time_to_sleep > _opts.WatchdogMaxSleepTime) { time_to_sleep = _opts.WatchdogMaxSleepTime; }

                    Thread.Sleep(new TimeSpan(0, 0, time_to_sleep));

                    lock (_fx_client_in_lock)
                    {
                        if ((_fx_client_in != null) && (_fx_client_in.IsLoggedIn))
                        {
                            _log.captureDebug("Watchdog determined connection is ok.");
                            continue;
                        }

                        // Something is amiss
                        _watchdog_restart_attempt_count++;
                        _log.captureError(
                            "Watchdog found data stream was not logged in. Attempting restart number '" +
                            _watchdog_restart_attempt_count + "'...", "watchdogMain Error");

                        if (_watchdog_restart_attempt_count > _opts.WatchdogMaxReconnectThreshold)
                        {
                            _log.captureError(
                                "Watchdog restart attempts '" + _watchdog_restart_attempt_count + "' exceed threshold '" +
                                _opts.WatchdogMaxReconnectThreshold + "'.", "watchdogMain Error");
                            return;
                        }

                        //store watched symbols...
                        List<Symbol> syms = new List<Symbol>();
                        foreach (RateTicker rt in _parent.RateTickers)
                        {
                            syms.Add(rt.Symbol);
                        }

                        //cleanup the now disconnected _in channel client...
                        if (_fx_client_in != null)
                        {
                            _fx_client_in.Destroy();
                            _fx_client_in = null;
                        }

                        FXClientResult fxres = connectIn(ServiceConnectOptions.LiveData, true);
                        if (fxres.Error)
                        {
                            _log.captureError("Watchdog reconnect error : " + fxres.Message, "watchdogMain Error");
                            continue;
                        }

                        //reload watched symbols...

                        if (!_parent.SetWatchedSymbols(syms))
                        {
                            //_parent will log errors...
                            return;
                        }
                    }

                    _watchdog_restart_complete_count++;
                    if (_watchdog_restart_complete_count > _watchdog_restart_complete_threshold)
                    {
                        _log.captureDebug("Watchdog restarts '" + _watchdog_restart_complete_count + "' exceed threshold '" + _watchdog_restart_complete_threshold + "'. Your connection is unstable!");
                    }
                    else
                    {
                        _log.captureDebug("Watchdog restarted connection.");
                    }
                    _watchdog_restart_attempt_count = 0;
                } while (true);
            }
            catch (ThreadAbortException)
            {
                //stopping watchdog...
            }
            catch (Exception e)
            {
                _log.captureError("Watchdog exception : " + e.Message, "watchdogMain Error");
            }
        }
        private FunctionResult stopDataWatchdog()
        {
            int jto = 10000;
            if (_watchdog_thread == null) { return new FunctionResult(); }
            if (_watchdog_thread.IsAlive) { _watchdog_thread.Abort(); }
            if (!_watchdog_thread.Join(jto))
            {
                _watchdog_thread = null;
                return FunctionResult.newError("Unable to join watchdog thread on shutdown.");
            }
            _watchdog_thread = null;
            return new FunctionResult();
        }
        
        public FXClientObjectResult<ArrayList> GetHistory(fxPair fxPair, Interval interval, int num_ticks,bool use_in_channel)
        {
            lock (_fx_client_in_lock)
            {
                FXClientObjectResult<ArrayList> res = new FXClientObjectResult<ArrayList>();
                fxClient fx_client = use_in_channel ? _fx_client_in : _fx_client_out;
                try
                {
                    if (!fx_client.WithRateThread)
                    {
                        res.setError("fx client has no rate table.", FXClientResponseType.Disconnected, false);
                        return res;
                    }
                    res.ResultObject = fx_client.RateTable.GetHistory(fxPair, interval, num_ticks);
                    if (res.ResultObject == null)
                    {
                        res.setError("Unable to fetch history.", FXClientResponseType.Invalid, false);
                        return res;
                    }
                    return res;
                }
                catch (SessionException oase)
                {
                    _log.captureException(oase);
                    res.setError(oase.Message, FXClientResponseType.Disconnected, true);
                    return res;
                }
                catch (OAException oae)
                {
                    _log.captureException(oae);
                    res.setError("General Oanda Exception : " + oae.Message, FXClientResponseType.Rejected, true);
                    return res;
                }
                catch (Exception e)
                {
                    _log.captureException(e);
                    res.setError("Unhandled Exception : " + e.Message, FXClientResponseType.Rejected, false);
                    return res;
                }
            }
        }

        private FXClientObjectResult<double> determineRate(bool is_buy, DateTime timestamp, string base_cur, string quote_cur)
        {
            FXClientObjectResult<double> res = new FXClientObjectResult<double>();
            string b_sym = base_cur + "/" + quote_cur;
            fxPair b_pair = new fxPair(b_sym);

            TimeSpan time_to_order = DateTime.UtcNow.Subtract(timestamp);
            int ticks_to_trans = (time_to_order.Seconds % 5) + 1;
            if (ticks_to_trans <= 0) { ticks_to_trans = 1; }//always try to get at least 1 tick...

            ArrayList b_rates;
            try
            {
                

                //***************
                //FIX ME - it would be much more efficient to store some of these rates for a few minutes after a lookup.
                //         checking the stored values first for a match, and only resolving via the rate table or history as needed.

                /////////////////////
                //if the timestamp is within 5 sec of now, just get the rate...
                //{
                #region rate resolution via rate table
                //fxTick b_tick = fx_client.RateTable.GetRate(b_pair);
                #endregion
                //}
                //else
                //{
                #region rate resolution via history
                _log.captureDebug("determineRate needs history for sym='" + b_sym + "' tick count='" + ticks_to_trans + "'");

                //array of fxHistoryPoints
                FXClientObjectResult<ArrayList> hres=GetHistory(b_pair, Interval.Every_5_Seconds, ticks_to_trans,false);
                if(hres.Error)
                {
                    res.setError(hres.Message,hres.FXClientResponse,hres.Disconnected);
                    return res;
                }

                b_rates = hres.ResultObject;

                int n;
                if (b_rates.Count < ticks_to_trans)
                {
                    _log.captureDebug("not enough history to get the real transaction timestamp rate, using last available tick instead.");
                    n = b_rates.Count - 1;
                }
                else
                {
                    n = ticks_to_trans - 1;
                }
                fxHistoryPoint hp = (fxHistoryPoint)b_rates[n];
                double b_ask=(hp.Open.Ask + hp.Close.Ask) / 2.0;
                double b_bid=(hp.Open.Bid + hp.Close.Bid) / 2.0;
                double trans_pr = is_buy ? b_ask : b_bid;
                res.ResultObject = trans_pr;
                _log.captureDebug("history point results pair='" + b_pair + "' timstamp='" + hp.Timestamp + "' avg BA (" + b_bid + "," + b_ask + ") is_buy='" + is_buy + "' trans_pr='" + trans_pr + "'");
                return res;
                #endregion
                //} end of rate vs history 'if', else statement
                /////////////////////
                //***************

            }
            catch (OAException oae)
            {
                _log.captureException(oae);
                res.setError("Oanda Exception : " + oae.Message);
                return res;
            }

        }

        public FXClientObjectResult<Fill> GenerateFillFromTransaction(Transaction trans, string act_currency)
        {
            FXClientObjectResult<Fill> res = new FXClientObjectResult<Fill>();
            if (!IsInit)
            {
                res.setError("fxClient is not initialized.", FXClientResponseType.Disconnected, true);
                return res;
            }

            Fill fill = new Fill();
            fill.FillDateTime = trans.Timestamp;

            double sym_pr = trans.Price;
            double act_pr = 1.0;

            // Symbol : Base/Quote
            if (trans.Base == act_currency)
            {
                act_pr = 1.0 / sym_pr;
            }
            else if (trans.Quote != act_currency)
            {//neither the Base nor the Quote is the base_currency
                bool found=false;
                double b_pr;
                FXClientObjectResult<double> b_res = null;

                if (!found) //determine Quote/act_currency cross price factor
                {
                    b_res = determineRate(trans.IsBuy(), trans.Timestamp, trans.Quote, act_currency);
                    if (!b_res.Error)
                    {
                        b_pr = b_res.ResultObject;
                        act_pr = b_pr;
                        found = true;
                        _log.captureDebug("converted sym price {" + trans.Base + "/" + trans.Quote + ":" + sym_pr + "} to account price {" + trans.Quote + "/" + act_currency + ":" + act_pr + "} using the cross lookup {" + trans.Quote + "/" + act_currency + " : " + b_pr + "}");
                    }
                    else
                    {
                        _log.captureDebug("determineRate error : " + b_res.Message);
                    }
                    if (b_res.Disconnected)
                    {
                        FXClientResult fxres=connectOut();
                        if (fxres.Error)
                        { res.setError("Unable to reconnect : " + fxres.Message, fxres.FXClientResponse, fxres.Disconnected); return res; }
                    }
                }

                if (!found) //determine act_currency/Quote cross price factor
                {
                    b_res = determineRate(trans.IsBuy(), trans.Timestamp, act_currency, trans.Quote);
                    if (!b_res.Error)
                    {
                        b_pr = b_res.ResultObject;
                        act_pr = (1.0 / b_pr);
                        found = true;
                        _log.captureDebug("converted sym price {" + trans.Base + "/" + trans.Quote + ":" + sym_pr + "} to account price {" + trans.Quote + "/" + act_currency + ":" + act_pr + "} using the cross lookup {" + act_currency + "/" + trans.Quote + " : " + b_pr + "}");
                    }
                    else
                    {
                        _log.captureDebug("determineRate error : " + b_res.Message);
                    }
                    if (b_res.Disconnected)
                    {
                        FXClientResult fxres = connectOut();
                        if (fxres.Error)
                        { res.setError("Unable to reconnect : " + fxres.Message, fxres.FXClientResponse, fxres.Disconnected); return res; }
                    }

                }
                
                if (!found) //determine a Base/act_currency cross price factor
                {
                    b_res = determineRate(trans.IsBuy(), trans.Timestamp, trans.Base, act_currency);
                    if (!b_res.Error)
                    {
                        b_pr = b_res.ResultObject;
                        act_pr = (1.0 / sym_pr) * b_pr;
                        found = true;
                        _log.captureDebug("converted sym price {" + trans.Base + "/" + trans.Quote + ":" + sym_pr + "} to account price {" + trans.Quote + "/" + act_currency + ":" + act_pr + "} using the cross factor {" + trans.Base + "/" + act_currency + " : " + b_pr + "}");
                    }
                    else
                    {
                        _log.captureDebug("determineRate error : " + b_res.Message);
                    }
                    if (b_res.Disconnected)
                    {
                        FXClientResult fxres = connectOut();
                        if (fxres.Error)
                        { res.setError("Unable to reconnect : " + fxres.Message, fxres.FXClientResponse, fxres.Disconnected); return res; }
                    }
                }

                if (!found) //determine act_currency/Base cross price factor
                {
                    b_res = determineRate(trans.IsBuy(), trans.Timestamp, act_currency, trans.Base);
                    if (!b_res.Error)
                    {
                        b_pr = b_res.ResultObject;
                        act_pr = 1.0 / (b_pr * sym_pr);
                        found = true;
                        _log.captureDebug("converted sym price {" + trans.Base + "/" + trans.Quote + ":" + sym_pr + "} to account price {" + trans.Quote + "/" + act_currency + ":" + act_pr + "} using the cross factor {" + act_currency + "/" + trans.Base + " : " + b_pr + "}");
                    }
                    else
                    {
                        _log.captureDebug("determineRate error : " + b_res.Message);
                    }
                    if (b_res.Disconnected)
                    {
                        FXClientResult fxres = connectOut();
                        if (fxres.Error)
                        { res.setError("Unable to reconnect : " + fxres.Message, fxres.FXClientResponse, fxres.Disconnected); return res; }
                    }
                }

                if (!found)
                {//error
                    res.setError("Unable to locate a suitable cross factor currency and/or price. " + b_res.Message, b_res.FXClientResponse, b_res.Disconnected);
                    return res;
                }
            }
            //else (trans.Quote == base_currency) act_pr = 1.0

            fill.Price = new Price(sym_pr, act_pr);
            fill.Quantity = (trans.Units < 0) ? (-1 * trans.Units) : trans.Units;
            res.ResultObject = fill;
            return res;
        }

        private bool outChannelIsInit { get { return (_fx_client_out != null && _fx_client_out.IsLoggedIn); } }


        public FXClientObjectResult<AccountResult> ConvertStringToAccount(string p)
        {
            FXClientObjectResult<AccountResult> res = new FXClientObjectResult<AccountResult>();
            AccountResult ares = new AccountResult();
            int act_i = -1;
            int act_id = -1;
            int r;
            if (string.IsNullOrEmpty(p)) { act_i = 0; }
            else if (int.TryParse(p, out r))
            {
                if (r < _smallest_account_num) { act_i = r; }
                else { act_id = r; }
            }
            else { act_i = 0; }

            lock (_fx_client_in_lock)
            {
                try
                {
                    if (!_fx_client_in.IsLoggedIn)
                    {
                        res.setError("Broker is not connected!", FXClientResponseType.Disconnected, true);
                        return res;
                    }

                    if (!outChannelIsInit)
                    {
                        FXClientResult cres = connectOut();
                        if (cres.Error)
                        {
                            res.setError(cres.Message, cres.FXClientResponse, cres.Disconnected);
                            return res;
                        }
                    }

                    if (act_id != -1)
                    {
                        ares.FromOutChannel = _fx_client_out.User.GetAccountWithId(act_id);
                        if (ares.FromOutChannel == null)
                        {
                            res.setError("Unable to locate oanda account object for account id '" + act_id + "'.", FXClientResponseType.Invalid, false);
                            return res;
                        }
                        res.ResultObject = ares;
                    }
                    else if (act_i != -1)
                    {
                        ArrayList arlist = _fx_client_out.User.GetAccounts();
                        if (act_i >= arlist.Count)
                        {
                            res.setError("Unable to locate oanda account object for account index '" + act_i + "'.", FXClientResponseType.Invalid, false);
                            return res;
                        }
                        ares.FromOutChannel = (Account)arlist[act_i];
                        res.ResultObject = ares;
                    }
                    else
                    {
                        res.setError("Unable to parse account string '" + p + "'.", FXClientResponseType.Rejected, false);
                        return res;
                    }


                    if (act_id != -1)
                    {
                        ares.FromInChannel = _fx_client_in.User.GetAccountWithId(act_id);
                        if (ares.FromInChannel == null)
                        {
                            res.setError("Unable to locate oanda account object for account id '" + act_id + "'.", FXClientResponseType.Invalid, false);
                            return res;
                        }
                    }
                    else if (act_i != -1)
                    {
                        ArrayList arlist = _fx_client_in.User.GetAccounts();
                        if (act_i >= arlist.Count)
                        {
                            res.setError("Unable to locate oanda account object for account index '" + act_i + "'.", FXClientResponseType.Invalid, false);
                            return res;
                        }
                        ares.FromInChannel = (Account)arlist[act_i];
                    }

                    return res;
                }
                catch (OrderException oe)
                {
                    _broker_log.captureException(oe);
                    res.setError("Oanda Order Exception : " + oe.Message, FXClientResponseType.Invalid, false);
                    return res;
                }
                catch (AccountException ae)
                {
                    _broker_log.captureException(ae);
                    res.setError("Oanda Account Exception : " + ae.Message, FXClientResponseType.Invalid, false);
                    return res;
                }
                catch (SessionException se)
                {
                    _broker_log.captureException(se);
                    res.setError("Oanda Session Exception : " + se.Message, FXClientResponseType.Disconnected, false);
                    return res;
                }
                catch (OAException oae)
                {
                    _broker_log.captureException(oae);
                    res.setError("General Oanda Exception : " + oae.Message, FXClientResponseType.Rejected, false);
                    return res;
                }
                catch (Exception e)
                {
                    _broker_log.captureException(e);
                    res.setError("Unhandled Exception : " + e.Message, FXClientResponseType.Rejected, false);
                    return res;
                }
            }
        }

        public FXClientResult AddAccountEventResponder(Account acct, AccountResponder ar)
        {
            FXClientResult res = new FXClientResult();
            try
            {
                _log.captureDebug("ERASEME LATER : Account values [currency=" + acct.HomeCurrency + " balance=" + acct.Balance.ToString() + "{margin: rate=" + acct.MarginRate.ToString() + " available=" + acct.MarginAvailable() + " used=" + acct.MarginUsed() + "}]");
                
                fxEventManager em = acct.GetEventManager();

                if (!em.add(ar))
                {
                    res.setError("Unable to add account responder to the oanda event manager.", FXClientResponseType.Invalid, false);
                }
                return res;
            }
            catch (AccountException ae)
            {
                _broker_log.captureException(ae);
                res.setError("Account Exception : " + ae.Message, FXClientResponseType.Invalid, false);
                return res;
            }
            catch (SessionException se)
            {
                _broker_log.captureException(se);
                res.setError("Session Exception : " + se.Message, FXClientResponseType.Disconnected, false);
                return res;
            }
            catch (OAException oae)
            {
                _broker_log.captureException(oae);
                res.setError("General Oanda Exception : " + oae.Message, FXClientResponseType.Rejected, false);
                return res;
            }
            catch (Exception e)
            {
                _broker_log.captureException(e);
                res.setError("Unhandled Exception : " + e.Message, FXClientResponseType.Rejected, false);
                return res;
            }
        }

        public FXClientObjectResult<MarketOrder> GetTradeWithID(int act_id, int trade_id)
        {
            FXClientObjectResult<MarketOrder> res;
            FXClientObjectResult<AccountResult> ares = ConvertStringToAccount(act_id.ToString());
            if (ares.Error)
            {
                res = new FXClientObjectResult<MarketOrder>();
                res.setError(ares.Message, ares.FXClientResponse, ares.Disconnected);
                return res;
            }
            return GetTradeWithID(ares.ResultObject.FromOutChannel, trade_id);
        }
        public FXClientObjectResult<MarketOrder> GetTradeWithID(Account acct, int trade_id)
        {
            FXClientObjectResult<MarketOrder> res = new FXClientObjectResult<MarketOrder>();

            try
            {
                MarketOrder mo = new MarketOrder();
                if (!outChannelIsInit)
                {
                    FXClientResult cres = connectOut();
                    if (cres.Error)
                    {
                        res.setError(cres.Message, cres.FXClientResponse, cres.Disconnected);
                        return res;
                    }
                }

                if (!acct.GetTradeWithId(mo, trade_id))
                {
                    res.setError("Unable to locate oanda market order for id '" + trade_id + "'.", FXClientResponseType.Invalid, false);
                    res.OrderMissing = true;
                    return res;
                }
                res.ResultObject = mo;
                return res;
            }
            catch (OrderException oe)
            {
                _broker_log.captureException(oe);
                res.setError("Oanda Order Exception : " + oe.Message, FXClientResponseType.Invalid, false);
                return res;
            }
            catch (AccountException ae)
            {
                _broker_log.captureException(ae);
                res.setError("Oanda Account Exception : " + ae.Message, FXClientResponseType.Invalid, false);
                return res;
            }
            catch (SessionException se)
            {
                _broker_log.captureException(se);
                res.setError("Oanda Session Exception : " + se.Message, FXClientResponseType.Disconnected, false);
                return res;
            }
            catch (OAException oae)
            {
                _broker_log.captureException(oae);
                res.setError("General Oanda Exception : " + oae.Message, FXClientResponseType.Rejected, false);
                if (oae.Message == "No data available") { res.OrderMissing = true; }
                return res;
            }
            catch (Exception e)
            {
                _broker_log.captureException(e);
                res.setError("Unhandled Exception : " + e.Message, FXClientResponseType.Rejected, false);
                return res;
            }
        }
        public FXClientObjectResult<LimitOrder> GetOrderWithID(int act_id, int id_num)
        {
            FXClientObjectResult<LimitOrder> res;
            FXClientObjectResult<AccountResult> ares = ConvertStringToAccount(act_id.ToString());
            if (ares.Error)
            {
                res = new FXClientObjectResult<LimitOrder>();
                res.setError(ares.Message, ares.FXClientResponse, ares.Disconnected);
                return res;
            }
            return GetOrderWithID(ares.ResultObject.FromOutChannel, id_num);
        }
        public FXClientObjectResult<LimitOrder> GetOrderWithID(Account acct, int id_num)
        {
            FXClientObjectResult<LimitOrder> res = new FXClientObjectResult<LimitOrder>();

            try
            {
                LimitOrder lo = new LimitOrder();
                if (!outChannelIsInit)
                {
                    FXClientResult cres = connectOut();
                    if (cres.Error)
                    {
                        res.setError(cres.Message, cres.FXClientResponse, cres.Disconnected);
                        return res;
                    }
                }

                if (!acct.GetOrderWithId(lo, id_num))
                {
                    res.setError("Unable to locate oanda market order for id '" + id_num + "'.", FXClientResponseType.Invalid, false);
                    res.OrderMissing = true;
                    return res;
                }
                res.ResultObject = lo;
                return res;
            }
            catch (OrderException oe)
            {
                _broker_log.captureException(oe);
                res.setError("Oanda Order Exception : " + oe.Message, FXClientResponseType.Invalid, false);
                return res;
            }
            catch (AccountException ae)
            {
                _broker_log.captureException(ae);
                res.setError("Oanda Account Exception : " + ae.Message, FXClientResponseType.Invalid, false);
                return res;
            }
            catch (SessionException se)
            {
                _broker_log.captureException(se);
                res.setError("Oanda Session Exception : " + se.Message, FXClientResponseType.Disconnected, false);
                return res;
            }
            catch (OAException oae)
            {
                _broker_log.captureException(oae);
                res.setError("General Oanda Exception : " + oae.Message, FXClientResponseType.Rejected, false);
                if (oae.Message == "No data available") { res.OrderMissing = true; }
                return res;
            }
            catch (Exception e)
            {
                _broker_log.captureException(e);
                res.setError("Unhandled Exception : " + e.Message, FXClientResponseType.Rejected, false);
                return res;
            }
        }

        public FXClientResult SendOAModify(Account acct, MarketOrder mo)
        {
            FXClientResult res = new FXClientResult();
            try
            {
                if (!outChannelIsInit)
                {
                    FXClientResult cres = connectOut();
                    if (cres.Error) { return cres; }
                    //FIX ME - acct will be invalid at this point...need to refetch it...
                }
                acct.Modify(mo);
                _broker_log.captureOAOut(acct, mo, "MODIFY");
                res.FXClientResponse = FXClientResponseType.Accepted;
                return res;
            }
            catch (OrderException oe)
            {
                _broker_log.captureException(oe);
                res.setError("Oanda Order Exception : {" + oe.Code + "} " + oe.Message, FXClientResponseType.Invalid, false);
                return res;
            }
            catch (AccountException ae)
            {
                _broker_log.captureException(ae);
                res.setError("Oanda Account Exception : {" + ae.Code + "} " + ae.Message, FXClientResponseType.Invalid, false);
                return res;
            }
            catch (SessionException oase)
            {
                _broker_log.captureException(oase);
                res.setError("Oanda Session Exception : {" + oase.Code + "} " + oase.Message, FXClientResponseType.Disconnected, false);
                return res;
            }
            catch (OAException e)
            {
                _broker_log.captureException(e);
                res.setError("Unable to modify order id '" + mo.Id + "' at oanda the servers : '{" + e.Code + "} " + e.Message + "'.", FXClientResponseType.Rejected, false);
                if (e.Message == "No data available") { res.OrderMissing = true; }
                return res;
            }
        }
        public FXClientResult SendOAModify(Account acct, LimitOrder lo)
        {
            FXClientResult res = new FXClientResult();
            try
            {
                if (!outChannelIsInit)
                {
                    FXClientResult cres = connectOut();
                    if (cres.Error) { return cres; }
                    //FIX ME - acct will be invalid at this point...need to refetch it...
                }
                acct.Modify(lo);
                _broker_log.captureOAOut(acct, lo, "MODIFY");
                res.FXClientResponse = FXClientResponseType.Accepted;
                return res;
            }
            catch (OrderException oe)
            {
                _broker_log.captureException(oe);
                res.setError("Oanda Order Exception : {" + oe.Code + "} " + oe.Message, FXClientResponseType.Invalid, false);
                return res;
            }
            catch (AccountException ae)
            {
                _broker_log.captureException(ae);
                res.setError("Oanda Account Exception : {" + ae.Code + "} " + ae.Message, FXClientResponseType.Invalid, false);
                return res;
            }
            catch (SessionException oase)
            {
                _broker_log.captureException(oase);
                res.setError("Oanda Session Exception : {" + oase.Code + "} " + oase.Message, FXClientResponseType.Disconnected, false);
                return res;
            }
            catch (OAException e)
            {
                _broker_log.captureException(e);
                res.setError("Unable to modify order id '" + lo.Id + "' at oanda the servers : '{" + e.Code + "} " + e.Message + "'.", FXClientResponseType.Rejected, false);
                if (e.Message == "No data available") { res.OrderMissing = true; }
                return res;
            }
        }
        public FXClientResult SendOAExecute(Account acct, MarketOrder mo)
        {
            FXClientResult res = new FXClientResult();
            try
            {
                if (!outChannelIsInit)
                {
                    FXClientResult cres = connectOut();
                    if (cres.Error) { return cres; }
                    //FIX ME - acct will be invalid at this point...need to refetch it...
                }
                acct.Execute(mo);
                _broker_log.captureOAOut(acct, mo, "EXECUTE");
                res.FXClientResponse = FXClientResponseType.Accepted;
                return res;
            }
            catch (OrderException oe)
            {
                _broker_log.captureException(oe);
                res.setError("Oanda Order Exception : {" + oe.Code + "} " + oe.Message, FXClientResponseType.Invalid, false);
                return res;
            }
            catch (AccountException ae)
            {
                _broker_log.captureException(ae);
                res.setError("Oanda Account Exception : {" + ae.Code + "} " + ae.Message, FXClientResponseType.Invalid, false);
                return res;
            }
            catch (SessionException oase)
            {
                _broker_log.captureException(oase);
                res.setError("Oanda Session Exception : {" + oase.Code + "} " + oase.Message, FXClientResponseType.Disconnected, false);
                return res;
            }
            catch (OAException e)
            {
                _broker_log.captureException(e);
                res.setError("Unable to submit market order to oanda the servers : '{" + e.Code + "} " + e.Message + "'.", FXClientResponseType.Rejected, false);
                return res;
            }
        }
        public FXClientResult SendOAExecute(Account acct, LimitOrder lo)
        {
            FXClientResult res = new FXClientResult();
            try
            {
                if (!outChannelIsInit)
                {
                    FXClientResult cres = connectOut();
                    if (cres.Error) { return cres; }
                    //FIX ME - acct will be invalid at this point...need to refetch it...
                }
                acct.Execute(lo);
                _broker_log.captureOAOut(acct, lo, "EXECUTE");
                res.FXClientResponse = FXClientResponseType.Accepted;
                return res;
            }
            catch (OrderException oe)
            {
                _broker_log.captureException(oe);
                res.setError("Oanda Order Exception : {" + oe.Code + "} " + oe.Message, FXClientResponseType.Invalid, false);
                return res;
            }
            catch (AccountException ae)
            {
                _broker_log.captureException(ae);
                res.setError("Oanda Account Exception : {" + ae.Code + "} " + ae.Message, FXClientResponseType.Invalid, false);
                return res;
            }
            catch (SessionException oase)
            {
                _broker_log.captureException(oase);
                res.setError("Oanda Session Exception : {" + oase.Code + "} " + oase.Message, FXClientResponseType.Disconnected, false);
                return res;
            }
            catch (OAException e)
            {
                _broker_log.captureException(e);
                res.setError("Unable to submit limit order to oanda the servers : '{" + e.Code + "} " + e.Message + "'.", FXClientResponseType.Rejected, false);
                return res;
            }
        }
        public FXClientResult SendOAClose(Account acct, MarketOrder mo)
        {
            FXClientResult res = new FXClientResult();
            try
            {
                if (!outChannelIsInit)
                {
                    FXClientResult cres = connectOut();
                    if (cres.Error) { return cres; }
                    //FIX ME - acct will be invalid at this point...need to refetch it...
                }
                acct.Close(mo);
                _broker_log.captureOAOut(acct, mo, "CLOSE");
                res.FXClientResponse = FXClientResponseType.Accepted;
                return res;
            }
            catch (OrderException oe)
            {
                _broker_log.captureException(oe);
                res.setError("Oanda Order Exception : {" + oe.Code + "} " + oe.Message, FXClientResponseType.Invalid, false);
                return res;
            }
            catch (AccountException ae)
            {
                _broker_log.captureException(ae);
                res.setError("Oanda Account Exception : {" + ae.Code + "} " + ae.Message, FXClientResponseType.Invalid, false);
                return res;
            }
            catch (SessionException oase)
            {
                _broker_log.captureException(oase);
                res.setError("Oanda Session Exception : {" + oase.Code + "} " + oase.Message, FXClientResponseType.Disconnected, false);
                return res;
            }
            catch (OAException e)
            {
                _broker_log.captureException(e);
                res.setError("Unable to close trade id '" + mo.Id + "' at oanda : '{" + e.Code + "} " + e.Message + "'.", FXClientResponseType.Rejected, false);
                if (e.Message == "No data available") { res.OrderMissing = true; }
                return res;
            }
        }
        public FXClientResult SendOAClose(Account acct, LimitOrder lo)
        {
            FXClientResult res = new FXClientResult();
            try
            {
                if (!outChannelIsInit)
                {
                    FXClientResult cres = connectOut();
                    if (cres.Error) { return cres; }
                    //FIX ME - acct will be invalid at this point...need to refetch it...
                }
                acct.Close(lo);
                _broker_log.captureOAOut(acct, lo, "CLOSE");
                res.FXClientResponse = FXClientResponseType.Accepted;
                return res;
            }
            catch (OrderException oe)
            {
                _broker_log.captureException(oe);
                res.setError("Oanda Order Exception : {" + oe.Code + "} " + oe.Message, FXClientResponseType.Invalid, false);
                return res;
            }
            catch (AccountException ae)
            {
                _broker_log.captureException(ae);
                res.setError("Oanda Account Exception : {" + ae.Code + "} " + ae.Message, FXClientResponseType.Invalid, false);
                return res;
            }
            catch (SessionException oase)
            {
                _broker_log.captureException(oase);
                res.setError("Oanda Session Exception : {" + oase.Code + "} " + oase.Message, FXClientResponseType.Disconnected, false);
                return res;
            }
            catch (OAException e)
            {
                _broker_log.captureException(e);
                res.setError("Unable to close trade id '" + lo.Id + "' at oanda : '{" + e.Code + "} " + e.Message + "'.", FXClientResponseType.Rejected, false);
                if (e.Message == "No data available") { res.OrderMissing = true; }
                return res;
            }
        }

   
        public FXClientObjectResult<double> GetMarginAvailable(Account account)
        {
            FXClientObjectResult<double> res = new FXClientObjectResult<double>();
            try
            {
                if (!outChannelIsInit)
                {
                    FXClientResult cres = connectOut();
                    if (cres.Error)
                    {
                        res.setError(cres.Message, cres.FXClientResponse, cres.Disconnected);
                        return res;
                    }
                }

                res.ResultObject = account.MarginAvailable();
                return res;
            }
            catch (SessionException oase)
            {
                _broker_log.captureException(oase);
                res.setError("Oanda Session Exception : {" + oase.Code + "} " + oase.Message, FXClientResponseType.Disconnected, false);
                return res;
            }
            catch (AccountException ae)
            {
                _broker_log.captureException(ae);
                res.setError("Oanda Account Exception : {" + ae.Code + "} " + ae.Message, FXClientResponseType.Invalid, false);
                return res;
            }
            catch (OAException oae)
            {
                _broker_log.captureException(oae);
                res.setError("Oanda General Exception : {" + oae.Code + "} " + oae.Message, FXClientResponseType.Rejected, false);
                return res;
            }
            catch (Exception e)
            {
                _broker_log.captureException(e);
                res.setError("Unhandled exception : " + e.Message, FXClientResponseType.Rejected, false);
                return res;
            }
        }
        public FXClientObjectResult<double> GetMarginUsed(Account account)
        {
            FXClientObjectResult<double> res = new FXClientObjectResult<double>();
            try
            {
                if (!outChannelIsInit)
                {
                    FXClientResult cres = connectOut();
                    if (cres.Error)
                    {
                        res.setError(cres.Message, cres.FXClientResponse, cres.Disconnected);
                        return res;
                    }
                }

                res.ResultObject = account.MarginUsed();
                return res;
            }
            catch (SessionException oase)
            {
                _broker_log.captureException(oase);
                res.setError("Oanda Session Exception : {" + oase.Code + "} " + oase.Message, FXClientResponseType.Disconnected, false);
                return res;
            }
            catch (AccountException ae)
            {
                _broker_log.captureException(ae);
                res.setError("Oanda Account Exception : {" + ae.Code + "} " + ae.Message, FXClientResponseType.Invalid, false);
                return res;
            }
            catch (OAException oae)
            {
                _broker_log.captureException(oae);
                res.setError("Oanda General Exception : {" + oae.Code + "} " + oae.Message, FXClientResponseType.Rejected, false);
                return res;
            }
            catch (Exception e)
            {
                _broker_log.captureException(e);
                res.setError("Unhandled exception : " + e.Message, FXClientResponseType.Rejected, false);
                return res;
            }
        }
        public FXClientObjectResult<double> GetMarginRate(Account account)
        {
            FXClientObjectResult<double> res = new FXClientObjectResult<double>();
            try
            {
                if (!outChannelIsInit)
                {
                    FXClientResult cres = connectOut();
                    if (cres.Error)
                    {
                        res.setError(cres.Message, cres.FXClientResponse, cres.Disconnected);
                        return res;
                    }
                }

                res.ResultObject = account.MarginRate;
                return res;
            }
            catch (SessionException oase)
            {
                _broker_log.captureException(oase);
                res.setError("Oanda Session Exception : {" + oase.Code + "} " + oase.Message, FXClientResponseType.Disconnected, false);
                return res;
            }
            catch (AccountException ae)
            {
                _broker_log.captureException(ae);
                res.setError("Oanda Account Exception : {" + ae.Code + "} " + ae.Message, FXClientResponseType.Invalid, false);
                return res;
            }
            catch (OAException oae)
            {
                _broker_log.captureException(oae);
                res.setError("Oanda General Exception : {" + oae.Code + "} " + oae.Message, FXClientResponseType.Rejected, false);
                return res;
            }
            catch (Exception e)
            {
                _broker_log.captureException(e);
                res.setError("Unhandled exception : " + e.Message, FXClientResponseType.Rejected, false);
                return res;
            }
        }
        public FXClientObjectResult<double> GetBalance(Account a)
        {
            FXClientObjectResult<double> res = new FXClientObjectResult<double>();
            try
            {
                if (!outChannelIsInit)
                {
                    FXClientResult cres = connectOut();
                    if (cres.Error)
                    {
                        res.setError(cres.Message, cres.FXClientResponse, cres.Disconnected);
                        return res;
                    }
                }

                res.ResultObject = a.Balance;
                return res;
            }
            catch (SessionException oase)
            {
                _broker_log.captureException(oase);
                res.setError("Oanda Session Exception : {" + oase.Code + "} " + oase.Message, FXClientResponseType.Disconnected, false);
                return res;
            }
            catch (AccountException ae)
            {
                _broker_log.captureException(ae);
                res.setError("Oanda Account Exception : {" + ae.Code + "} " + ae.Message, FXClientResponseType.Invalid, false);
                return res;
            }
            catch (OAException oae)
            {
                _broker_log.captureException(oae);
                res.setError("Oanda General Exception : {" + oae.Code + "} " + oae.Message, FXClientResponseType.Rejected, false);
                return res;
            }
            catch (Exception e)
            {
                _broker_log.captureException(e);
                res.setError("Unhandled exception : " + e.Message, FXClientResponseType.Rejected, false);
                return res;
            }
        }

        public FXClientObjectResult<List<AccountResult>> GetFullAccountsList()
        {
            List<AccountResult> act_list = new List<AccountResult>();
            FXClientObjectResult<List<AccountResult>> res = new FXClientObjectResult<List<AccountResult>>();

            try
            {
                lock (_fx_client_in_lock)
                {
                    if (!_fx_client_in.IsLoggedIn)
                    {
                        res.setError("Broker is not connected!", FXClientResponseType.Disconnected, true);
                        return res;
                    }

                    ArrayList in_alist = _fx_client_in.User.GetAccounts();
                    foreach (Account a in in_alist)
                    {
                        AccountResult ar = new AccountResult();
                        ar.FromInChannel = a;
                        act_list.Add(ar);
                    }
                }

                //check the connection on the out channel...
                if (!outChannelIsInit)
                {
                    FXClientResult cres = connectOut();
                    if (cres.Error)
                    {
                        res.setError(cres.Message, cres.FXClientResponse, cres.Disconnected);
                        return res;
                    }
                }

                ArrayList out_alist = _fx_client_out.User.GetAccounts();

                if (out_alist.Count != act_list.Count)
                {//account list mismatch
                    res.setError("The input and output channel account count mismatch. in{" + act_list.Count + "} != out{" + out_alist.Count + "}");
                    return res;
                }

                for (int i = 0; i < out_alist.Count; i++)
                {
                    Account a = (Account)out_alist[i];

                    Account ina = act_list[i].FromInChannel;
                    if (ina == null)
                    {
                        res.setError("AccountResult list element missing Account object.");
                        return res;
                    }
                    if (ina.AccountId != a.AccountId)
                    {//account lists are in different orders...
                        res.setError("Account lists are in different orders on the channels....need to implement a better match algorythm...");
                        return res;
                    }
                    act_list[i].FromOutChannel = a;
                }

                res.ResultObject = act_list;
                return res;
            }
            catch (SessionException se)
            {
                _broker_log.captureException(se);
                res.setError("Oanda Session Exception : " + se.Message,FXClientResponseType.Disconnected,false);
                return res;
            }
            catch (AccountException ae)
            {
                _broker_log.captureException(ae);
                res.setError("Oanda Account Exception : " + ae.Message, FXClientResponseType.Invalid, false);
                return res;
            }
            catch (OAException oae)
            {
                _broker_log.captureException(oae);
                res.setError("Oanda General Exception : " + oae.Message, FXClientResponseType.Rejected, false);
                return res;
            }
        }

    }

    public class ResponseProcessor
    {
        public ResponseProcessor() { }
        public ResponseProcessor(OandAPlugin parent, BrokerLog log) { _log = log; _parent = parent; }

        private BrokerLog _log = null;
        [XmlIgnore]
        public BrokerLog BrokerLog { set { _log = value; } get { return (_log); } }

        private OandAPlugin _parent = null;
        [XmlIgnore]
        public OandAPlugin Parent { set { _parent = value; } get { return (_parent); } }

        private int _transaction_retry_max = 5;
        private Dictionary<int, AccountResponder> _account_responders = new Dictionary<int, AccountResponder>();//key : account number
        private List<ResponseRecord> _response_pending_list = new List<ResponseRecord>();
        private Thread _response_processor = null;
        private bool _waiting = false;

        private int _join_timeout_ms = 15000;

        public void Start()
        {
            if (_response_processor != null)
            {
                throw new OAPluginException("responseProcessorStart called on existing thread");
            }
            _response_processor = new Thread(threadMain);
            _response_processor.Name = "OandA Response Processor";
            _response_processor.Start();
        }
        public void Stop()
        {
            if (_response_processor == null) { return; }
            _response_processor.Abort();
            if (!_response_processor.Join(_join_timeout_ms))
            {
                _log.captureError("Unable to join with response processing thread for shutdown.", "ResponseProcessor Error");
            }
            _response_processor = null;
        }
        private void threadMain()
        {
            _log.captureDebug("responseProcessorMain() called.");
            try
            {//spin on sub main until thread abort
                while (threadBody()) ;
            }
            catch (ThreadAbortException)
            {
                //cleanup
            }
        }

        //returns true if interupted and in need of restart
        //returns false if aborted and in need of shutdown
        private bool threadBody()
        {
            ResponseRecord r;
            try
            {
                _log.captureDebug("responseProcessorSubMain() called.");

                while (true)
                {
                    if (_response_pending_list.Count == 0)
                    {//sleep untill pending messages
                        bool do_continue = false;
                        try
                        {
                            _waiting = true;
                            Thread.Sleep(Timeout.Infinite);
                        }
                        catch (ThreadInterruptedException)
                        {
                            do_continue = true;
                        }
                        _waiting = false;
                        if (do_continue) { continue; }
                        _response_processor.Abort();
                    }

                    Thread.BeginCriticalRegion();

                    Monitor.Enter(_response_pending_list);
                    try
                    {
                        r = _response_pending_list[0];
                        _response_pending_list.RemoveAt(0);
                    }
                    finally { Monitor.Pulse(_response_pending_list); Monitor.Exit(_response_pending_list); }

                    FXClientTaskResult res = _parent.OrderBook.HandleAccountTransaction(r);
                    if (res.Error)
                    {
                        _log.captureError(res.Message, "responseProcessorSubMain Error");

                        if (res.Disconnected)
                        {
                            _parent.fxClient.Disconnect();
                        }
                    }

                    if (!res.TaskCompleted)
                    {
                        if (r.RetryCount > _transaction_retry_max)
                        {
                            _log.captureError("transaction response retry count exceeded, dropping it", "responseProcessorSubMain Error");
                        }
                        else
                        {
                            Monitor.Enter(_response_pending_list);
                            try
                            {
                                r.RetryCount++;
                                _response_pending_list.Insert(0, r);
                            }
                            finally { Monitor.Pulse(_response_pending_list); Monitor.Exit(_response_pending_list); }
                        }
                    }

                    Thread.EndCriticalRegion();

                    _parent.OrderBook.ClearAllFinalizedPositions();
                }
            }
            catch (ThreadInterruptedException)
            {
                _log.captureDebug("responseProcessorSubMain Interrupted");
                return true;
            }
            catch (ThreadAbortException)
            {
                _log.captureDebug("responseProcessorSubMain Aborted");
                return false;
            }
        }

        private List<ResponseRecord> _receive_queue = new List<ResponseRecord>();
        public void HandleAccountResponder(AccountResponder ar, fxAccountEventInfo aei, fxEventManager em)
        {
            _log.captureDebug("handleAccountResponder() called.");
            Transaction trans = (Transaction)aei.Transaction.Clone();

            _log.captureOAIn(trans, ar.AccountID);

            ResponseRecord resp = new ResponseRecord(trans, ar.AccountID, ar.BaseCurrency);
            _receive_queue.Add(resp);

            if (!Monitor.TryEnter(_response_pending_list))
            { return; }

            try
            {
                foreach (ResponseRecord rr in _receive_queue)
                { _response_pending_list.Add(rr); }
            }
            finally { Monitor.Pulse(_response_pending_list); Monitor.Exit(_response_pending_list); }

            if (_waiting && _response_processor.ThreadState == ThreadState.WaitSleepJoin)
            {
                _response_processor.Interrupt();
            }

            _receive_queue.Clear();
        }

        public FXClientTaskResult ActivateAccountResponder(Account acct)
        {
            FXClientTaskResult res = new FXClientTaskResult();
            int aid = acct.AccountId;

            if (_account_responders.ContainsKey(aid))
            {
                if (!_account_responders[aid].Active)
                {
                    _account_responders[aid].Active = true;
                    res.TaskCompleted = true;
                    return res;
                }
                return res;
            }

            AccountResponder ar = new AccountResponder(aid, acct.HomeCurrency, _parent);
            FXClientResult addres = _parent.fxClient.AddAccountEventResponder(acct, ar);
            if (addres.Error)
            {
                res.setError(addres.Message, addres.FXClientResponse, addres.Disconnected);
                return res;
            }

            _account_responders.Add(aid, ar);
            res.TaskCompleted = true;
            return res;
        }
        public FXClientTaskResult ActivateAccountResponder(int aid)
        {
            FXClientTaskResult res = new FXClientTaskResult();
            if (_account_responders.ContainsKey(aid))
            {
                if (!_account_responders[aid].Active)
                {
                    _account_responders[aid].Active = true;
                    res.TaskCompleted = true;
                    return res;
                }
                return res;
            }

            FXClientObjectResult<AccountResult> ares = _parent.fxClient.ConvertStringToAccount(aid.ToString());
            if (ares.Error)
            {
                res.setError(ares.Message, ares.FXClientResponse, ares.Disconnected);
                return res;
            }

            Account acct = ares.ResultObject.FromInChannel;
            return ActivateAccountResponder(acct);
        }


        public void DeactivateAccountResponder(int aid)
        {
            if (_account_responders.ContainsKey(aid))
            {
                if (_account_responders[aid].Active)
                {
                    _account_responders[aid].Active = false;
                }
            }
        }
        public void ClearAccountResponders()
        {
            _account_responders.Clear();
        }
        public List<int> GetActiveAccountResponders()
        {
            List<int> id_list = new List<int>();

            _log.captureDebug("account responder dictionary contains '" + _account_responders.Keys.Count + "' keys");
            foreach (int i in _account_responders.Keys)
            {
                _log.captureDebug(" key[" + i + "] : value [active='" + _account_responders[i].Active + "',account='" + _account_responders[i].AccountID + "',basecur='" + _account_responders[i].BaseCurrency + "'");
                if (_account_responders[i].Active)
                {
                    _log.captureDebug("adding account listener for account '" + i + "' to active responder list.");
                    id_list.Add(i);
                }
            }
            return id_list;
        }
    }

    #region Trade Record Classes
    public enum IDType { Other, Stop, Target, Close, Fail };

    [Serializable]
    public class IDString : IEquatable<IDString>
    {
        public IDString() { }
        public IDString(string s) { ID = s; }
        public IDString(IDType t, int onum) { _type = t; _order_num = onum; _sub_num = 0; }
        public IDString(IDType t, int onum, int snum) { _type = t; _order_num = onum; _sub_num = snum; }

        public override int GetHashCode()
        {
            return ID.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if (System.Object.ReferenceEquals(this, obj)) { return true; }
            if (!(obj is IDString)) { return false; }
            return Equals((IDString)obj);
        }
        public bool Equals(IDString obj)
        {
            if (System.Object.ReferenceEquals(this, obj)) { return true; }
            if (ID == obj.ID) { return true; }
            return false;
        }

        private string typeString()
        {
            switch (_type)
            {
                case IDType.Other: return "";
                case IDType.Close: return "close";
                case IDType.Target: return "ptarget";
                case IDType.Stop: return "pstop";
                case IDType.Fail: return "fail";
                default:
                    throw new OAPluginException("Unknown IDString type prefix '" + _type + "'.");
            }
        }

        [XmlAttribute("IDStringValue")]
        public string ID
        {
            get { return (((_type == IDType.Other) ? "" : (_type == IDType.Other ? "" : (typeString() + "-"))) + _order_num.ToString() + (_sub_num == 0 ? "" : ("-" + _sub_num.ToString()))); }
            set
            {
                int r;
                if (value.Contains("-"))
                {
                    char[] c = new char[1];
                    c[0] = '-';
                    string[] res = value.Split(c, 3);
                    if (res.Length != 2 && res.Length != 3)
                    {
                        throw new OAPluginException("Unable to break id string on delimiter.");
                    }

                    if (res[0] == "pstop") { _type = IDType.Stop; }
                    else if (res[0] == "ptarget") { _type = IDType.Target; }
                    else if (res[0] == "close") { _type = IDType.Close; }
                    else
                    {
                        throw new OAPluginException("Unable to parse id type component '" + res[2] + "'");
                    }

                    r = 0;
                    if (res.Length == 3 && !int.TryParse(res[2], out r))
                    {
                        throw new OAPluginException("Unable to parse id sub-number component '" + res[2] + "'");
                    }
                    _sub_num = r;

                    if (!int.TryParse(res[1], out r))
                    {
                        throw new OAPluginException("Unable to parse id number component '" + res[1] + "'");
                    }
                }
                else
                {
                    if (!int.TryParse(value, out r))
                    {
                        throw new OAPluginException("Unable to parse id number component '" + value + "'");
                    }
                    _type = IDType.Other;
                    _sub_num = 0;
                }
                _order_num = r;
            }
        }

        private IDType _type = IDType.Other;
        [XmlIgnore]
        public IDType Type { set { _type = value; } get { return (_type); } }

        private int _order_num;
        [XmlIgnore]
        public int Num { set { _order_num = value; } get { return (_order_num); } }

        private int _sub_num = 0;
        [XmlIgnore]
        public int SubNum { set { _sub_num = value; } get { return (_sub_num); } }
    }


    [Serializable]
    public class ResponseRecord
    {
        public ResponseRecord(Transaction trans, int aid, string cur) { _act_id = aid; _trans = trans; _base_currency = cur; }
        public ResponseRecord() { }

        private int _act_id = 0;
        public int AccountId { set { _act_id = value; } get { return (_act_id); } }

        private int _retry_count = 0;
        public int RetryCount { set { _retry_count = value; } get { return (_retry_count); } }

        private string _base_currency = string.Empty;
        public string BaseCurrency { get { return (_base_currency); } }

        private Transaction _trans = null;
        public Transaction Transaction { set { _trans = value; } get { return (_trans); } }
    }

    public class FillList
    {
        private List<FillRecord> _list = new List<FillRecord>();
        public List<FillRecord> List { get { return (_list); } }
        private int _linkless_count = 0;
        public int LinklessCount { get { return (_linkless_count); } }

        public void IncreaseLinklessCount()
        {
            _linkless_count++;
        }
        public void DecreaseLinklessCount()
        {
            _linkless_count--;
        }
    }

    [Serializable]
    public class FillRecord
    {
        public FillRecord() { }
        public FillRecord(Fill f, string n_id, string l_id) { _fill = f; _id = n_id; _link_id = l_id; }

        private Fill _fill = null;
        public Fill Fill { set { _fill = value; } get { return (_fill); } }

        private string _id = null;
        public string Id { set { _id = value; } get { return (_id); } }
        private string _link_id = null;
        public string LinkId { set { _link_id = value; } get { return (_link_id); } }
    }

    public class FillQueue
    {
        private RightEdgeOandaPlugin.SerializableDictionary<string, FillList> _queues = new RightEdgeOandaPlugin.SerializableDictionary<string, FillList>();

        public void AddNewFillRecord(string pair, Fill fill, int id, int link)
        {
            if (!_queues.ContainsKey(pair))
            {
                _queues[pair] = new FillList();
            }
            _queues[pair].List.Add(new FillRecord(fill, id.ToString(), link.ToString()));
            if (link == 0) { _queues[pair].IncreaseLinklessCount(); }
        }


        public FunctionObjectResult<FillRecord> FindOpenFill(string sym, string id, int units, PluginLog log)
        {
            FunctionObjectResult<FillRecord> res = new FunctionObjectResult<FillRecord>();
            
            //to match an unfilled limit order, look for the fill record with a 0 link (if more than 1, it's an error)
            FillList fill_queue;
            if (!_queues.TryGetValue(sym, out fill_queue))
            {
                res.setError("No fill queue found for symbol '" + sym + "'");
                return res;
            }

            if (fill_queue.LinklessCount == 0)
            {
                if (fill_queue.List.Count != 1)
                {
                    res.setError("No linkless fills, but multiple fill records in queue for symbol '" + sym + "'");
                    return res;
                }
                FillRecord fr = fill_queue.List[0];
                log.captureDebug("      only fill {id=" + fr.Id + ",link=" + fr.LinkId + "} qty='" + fr.Fill.Quantity + "'");
                log.captureDebug("        using this fill");
                fill_queue.List.Remove(fr);
                fill_queue.DecreaseLinklessCount();
                if (fill_queue.List.Count == 0) { _queues.Remove(sym); }

                res.ResultObject = fr;
                return res;
            }

            if (fill_queue.LinklessCount != 1)
            {
                res.setError("Wrong linkless fill count for symbol '" + sym + "' count='" + fill_queue.LinklessCount + "'");
                return res;
            }

            foreach (FillRecord fr in fill_queue.List)
            {
                log.captureDebug("      checking fill {id=" + fr.Id + ",link=" + fr.LinkId + "} qty='" + fr.Fill.Quantity + "'");

                if (fr.LinkId == "0")
                {
                    log.captureDebug("        using this fill");
                    fill_queue.List.Remove(fr);
                    fill_queue.DecreaseLinklessCount();
                    if (fill_queue.List.Count == 0) { _queues.Remove(sym); }

                    res.ResultObject = fr;
                    return res;
                }
            }

            res.setError("No fill record for the order id '" + id + "' symbol '" + sym + "'.");
            return res;
        }

        public int QueueCount(string p)
        {
            return _queues.ContainsKey(p) ? _queues[p].List.Count : 0;
        }

        public int QueueUnitTotal(string p)
        {
            int n = 0;
            if (!_queues.ContainsKey(p)) { return n; }
            foreach (FillRecord fr in _queues[p].List) { n += fr.Fill.Quantity; }
            return n;
        }

        public FunctionObjectResult<FillRecord> GetNextFill(string p)
        {
            FunctionObjectResult<FillRecord> fres = new FunctionObjectResult<FillRecord>();
            if (!_queues.ContainsKey(p))
            {
                fres.setError("No fill record queue found for symbol '" + p + "'");
                return fres;
            }
            foreach (FillRecord fr in _queues[p].List) { fres.ResultObject = fr; _queues[p].List.Remove(fr); return fres; }
            fres.setError("No fill records found in queue for symbol '" + p + "'");
            return fres;
        }
    }

    [Serializable]
    public class OpenOrderRecord : OrderRecord
    {
        public OpenOrderRecord() { }
        public OpenOrderRecord(BrokerOrder bo, bool is_re) : base(bo, is_re) { }

        private string _fill_id = null;
        public string FillId { set { _fill_id = value; } get { return (_fill_id); } }

        private double _stop_price = 0.0;
        public double StopPrice { set { _stop_price = value; } get { return (_stop_price); } }

        private bool _stop_hit = false;
        public bool StopHit { set { _stop_hit = value; } get { return (_stop_hit); } }

        private double _target_price = 0.0;
        public double TargetPrice { set { _target_price = value; } get { return (_target_price); } }

        private bool _target_hit = false;
        public bool TargetHit { set { _target_hit = value; } get { return (_target_hit); } }
    }

    [Serializable]
    public class OrderRecord
    {
        public OrderRecord() { }
        public OrderRecord(BrokerOrder bo, bool is_re) { _order = bo; _is_right_edge_order = is_re; }

        private BrokerOrder _order = null;
        [XmlIgnore] //FIX ME - there are values in this needed to re-sync the account state on load
        public BrokerOrder BrokerOrder { set { _order = value; } get { return (_order); } }

        private BrokerOrderState _bo_state = BrokerOrderState.Invalid;
        public BrokerOrderState BrokerOrderState { get { return (_order == null ? _bo_state : _order.OrderState); } set { _bo_state = value; if (_order != null) { _order.OrderState = value; } } }

        private OrderType _o_type = OrderType.PeggedToMarket;
        public OrderType OrderType { get { return (_order == null ? _o_type : _order.OrderType); } set { _o_type = value; if (_order != null) { _order.OrderType = value; } } }

        private TransactionType _t_type = TransactionType.Interest;
        public TransactionType TransactionType { get { return (_order == null ? _t_type : _order.TransactionType); } set { _t_type = value; if (_order != null) { _order.TransactionType = value; } } }

        private long _o_size = 0;
        public long OrderSize { get { return (_order == null ? _o_size : _order.Shares); } set { _o_size = value; if (_order != null) { _order.Shares = value; } } }

        private string _o_id = string.Empty;
        public string OrderID { get { return (_order == null ? _o_id : _order.OrderId); } set { _o_id = value; if (_order != null) { _order.OrderId = value; } } }


        private int _fill_qty = 0;
        public int FillQty { set { _fill_qty = value; } get { return (_fill_qty); } }

        private bool _is_right_edge_order = false;
        public bool IsRightEdgeOrder { set { _is_right_edge_order = value; } get { return (_is_right_edge_order); } }

        private bool _cancel_to_close = false;
        public bool CancelToClose { set { _cancel_to_close = value; } get { return (_cancel_to_close); } }
    }

    [Serializable]
    public class TradeRecord
    {
        public TradeRecord() { }
        public TradeRecord(IDString id) { _id = id; }
        public TradeRecord(string id) { _id = new IDString(id); }
        public TradeRecord(IDString id, BrokerOrder open_order, bool is_re) { _id = id; _open_order = new OpenOrderRecord(open_order, is_re); }
        public TradeRecord(string id, BrokerOrder open_order, bool is_re) { _id = new IDString(id); _open_order = new OpenOrderRecord(open_order, is_re); }

        private IDString _id = null;
        public IDString OrderID { set { _id = value; } get { return (_id); } }

        public FunctionObjectResult<int> IDNumber()
        {
            FunctionObjectResult<int> res = new FunctionObjectResult<int>();
            if (openOrder == null || openOrder.BrokerOrder == null)
            {
                res.setError("Unable to resolve id number, missing open order broker record.");
                return res;
            }

            OrderType ot = openOrder.BrokerOrder.OrderType;
            int id_num;
            if (ot == OrderType.Limit)
            { id_num = string.IsNullOrEmpty(openOrder.FillId) ? 0 : int.Parse(openOrder.FillId); }
            else if (ot == OrderType.Market)
            { id_num = OrderID.Num; }
            else
            {
                res.setError("Unable to resolve id number on open order type '" + ot + "'.");
                return res;
            }
            res.ResultObject = id_num;
            return res;
        }

        private OpenOrderRecord _open_order = null;
        public OpenOrderRecord openOrder { set { _open_order = value; } get { return (_open_order); } }

        private OrderRecord _close_order = null;
        public OrderRecord closeOrder { set { _close_order = value; } get { return (_close_order); } }

        public void ClearREOwned()
        {
            if (_open_order != null) { _open_order.IsRightEdgeOrder = false; }
            if (_close_order != null) { _close_order.IsRightEdgeOrder = false; }
        }
    }

    [Serializable]
    public class TradeRecords : RightEdgeOandaPlugin.SerializableDictionary<IDString, TradeRecord>
    {//key is orderid of tr.openorder
        public TradeRecords() { }
        public TradeRecords(IDString id) { _id = id; }

        private IDString _id;
        [XmlElement("IDValue")]
        public IDString ID { set { _id = value; } get { return (_id); } }
    }

    [Serializable]
    public class BrokerPositionRecord
    {
        public BrokerPositionRecord() { }
        public BrokerPositionRecord(string id) { _id = id; }

        private string _id;
        [XmlElement("IDValue")]
        public string ID { set { _id = value; } get { return (_id); } }

        private Symbol _sym = null;
        public Symbol Symbol { set { _sym = value; } get { return (_sym); } }

        private PositionType _pt;
        public PositionType Direction { set { _pt = value; } get { return (_pt); } }

        private int _stop_num = 1;
        public int StopNumber { set { _stop_num = value; } get { return (_stop_num); } }

        private int _target_num = 1;
        public int TargetNumber { set { _target_num = value; } get { return (_target_num); } }

        private OrderRecord _stop_order = null;
        public OrderRecord StopOrder { set { _stop_order = value; } get { return (_stop_order); } }

        private OrderRecord _target_order = null;
        public OrderRecord TargetOrder { set { _target_order = value; } get { return (_target_order); } }

        private OrderRecord _close_order = null;
        public OrderRecord CloseOrder { set { _close_order = value; } get { return (_close_order); } }

        private TradeRecords _tr_dict = new TradeRecords();
        public TradeRecords TradeRecords { set { _tr_dict = value; } get { return (_tr_dict); } }

        public FunctionResult pushTrade(BrokerOrder open_order, bool is_re)
        {
            IDString order_id = new IDString(open_order.OrderId);
            Monitor.Enter(_tr_dict);
            try
            {
                FunctionResult res = new FunctionResult();
                if (_tr_dict.ContainsKey(order_id))
                {
                    res.setError("The order id '" + order_id.ID + "' already exists, unable to add trade record.");
                    return res;
                }
                _tr_dict[order_id] = new TradeRecord(order_id, open_order, is_re);
                return res;
            }
            finally { Monitor.Pulse(_tr_dict); Monitor.Exit(_tr_dict); }
        }

        public void ClearREOwned()
        {
            if (_stop_order != null) { _stop_order.IsRightEdgeOrder = false; }
            if (_target_order != null) { _target_order.IsRightEdgeOrder = false; }
            if (_close_order != null) { _close_order.IsRightEdgeOrder = false; }

            foreach (IDString ids in _tr_dict.Keys)
                { _tr_dict[ids].ClearREOwned(); }
        }

        public FunctionResult VerifyREOwned()
        {
            FunctionResult fres = new FunctionResult();

            if (_close_order != null && !_close_order.IsRightEdgeOrder)
            {
                fres.setError("Position record close order is not a RightEdge owned broker order.");
                return fres;
            }
            if (_stop_order != null && !_stop_order.IsRightEdgeOrder)
            {
                fres.setError("Position record stop order is not a RightEdge owned broker order.");
                return fres;
            }
            if (_target_order != null && !_target_order.IsRightEdgeOrder)
            {
                fres.setError("Position record target order is not a RightEdge owned broker order.");
                return fres;
            }

            foreach (IDString trk in TradeRecords.Keys)
            {
                TradeRecord tr = TradeRecords[trk];

                if (tr.openOrder.BrokerOrderState == BrokerOrderState.Filled)
                {//the order is open...
                    //tr open can be RE

                    if (tr.closeOrder != null && tr.closeOrder.IsRightEdgeOrder)
                    {//tr close should not be RE
                        fres.setError("Trade record close order is a RightEdge owned broker order.");
                        return fres;
                    }
                }
                if (tr.openOrder.BrokerOrderState == BrokerOrderState.PendingCancel)
                {//the order was being canceled...
                    fres.setError("Trade record open order was being canceled!");
                    return fres;
                }
                if (tr.openOrder.BrokerOrderState == BrokerOrderState.Submitted)
                {//the order is waiting...
                    if (!tr.openOrder.IsRightEdgeOrder)
                    {//tr open must be RE
                        fres.setError("Trade record open orders for submitted orders must have a RightEdge owned broker order.");
                        return fres;
                    }
                    if (tr.closeOrder != null && tr.closeOrder.IsRightEdgeOrder)
                    {//tr close should not be RE
                        fres.setError("Trade record close order is a RightEdge owned broker order.");
                        return fres;
                    }
                }
            }

            return fres;
        }
    }

    #region ID matching classes
    public class OrderIDRecord
    {
        public IDString OrderID = null;
        public string FillID = string.Empty;

        public OrderIDRecord() { }
        public OrderIDRecord(TradeRecord tr) { InitFromTradeRecord(tr); }

        public void InitFromTradeRecord(TradeRecord tr)
        {
            OrderID = tr.OrderID;

            if (tr.openOrder != null && !string.IsNullOrEmpty(tr.openOrder.FillId))
            { FillID = tr.openOrder.FillId; }
            else
            { FillID = string.Empty; }
        }

        public TransactionRecordResult transMatch(int trans_num, int link_num)
        {
            TransactionRecordResult res = new TransactionRecordResult();

            if (OrderID == null)
            {
                res.MatchType = TransactionMatchType.None;
                return res;
            }

            int id_num = OrderID.Num;
            int fill_id_num = string.IsNullOrEmpty(FillID) ? 0 : int.Parse(FillID);

            if (id_num == trans_num)
            {
                res.PositionExists = true;
                res.OrderId = OrderID;
                res.MatchType = TransactionMatchType.Trans;
            }
            else if (link_num != 0 && (link_num == id_num || link_num == fill_id_num))
            {
                res.PositionExists = true;
                res.OrderId = OrderID;
                res.MatchType = TransactionMatchType.Link;
            }
            else
            {
                res.MatchType = TransactionMatchType.None;
            }
            return res;
        }
    }

    public class PositionIDRecord
    {
        public PositionIDRecord() { }
        public PositionIDRecord(BrokerPositionRecord bpr) { InitFromPosition(bpr); }

        public TransactionRecordResult ContainsId(string id)
        {
            TransactionRecordResult trr = new TransactionRecordResult();

            if (PositionId == id)
            {
                trr.MatchType = TransactionMatchType.Position;
                trr.PositionId = id;
                trr.PositionExists = true;
                return trr;
            }

            foreach (OrderIDRecord oidr in OrderIdList)
            {
                if (oidr.OrderID.ID == id)
                {
                    trr.MatchType = TransactionMatchType.Trans;
                    trr.PositionId = PositionId;
                    trr.PositionExists = true;
                    trr.OrderId = oidr.OrderID;
                    return trr;
                }
            }
            return trr;
        }

        public void InitFromPosition(BrokerPositionRecord bpr)
        {
            PositionId = bpr.ID;

            OrderIdList.Clear();
            foreach (IDString tr_key in bpr.TradeRecords.Keys)
            {
                OrderIdList.Add(new OrderIDRecord(bpr.TradeRecords[tr_key]));
            }
        }

        public TransactionRecordResult ContainsTrans(int trans_num, int link_num)
        {
            TransactionRecordResult trr;
            foreach (OrderIDRecord oidr in OrderIdList)
            {
                trr = oidr.transMatch(trans_num, link_num);
                if (trr.MatchType != TransactionMatchType.None)
                {
                    trr.PositionId = PositionId;
                    return trr;
                }
            }
            return new TransactionRecordResult();
        }

        public string PositionId;

        public List<OrderIDRecord> OrderIdList = new List<OrderIDRecord>();
    }
    #endregion

    [Serializable]
    public class BrokerPositionRecords
    {
        public BrokerPositionRecords(string id) { _id = id; }
        public BrokerPositionRecords() { }

        private string _id;
        public string SymbolID { set { _id = value; } get { return (_id); } }

        private PositionType _dir;
        public PositionType Direction { set { _dir = value; } get { return (_dir); } }

        private RightEdgeOandaPlugin.SerializableDictionary<string, BrokerPositionRecord> _positions = new RightEdgeOandaPlugin.SerializableDictionary<string, BrokerPositionRecord>();
        public RightEdgeOandaPlugin.SerializableDictionary<string, BrokerPositionRecord> Positions { set { _positions = value; } get { return (_positions); } }

        public TransactionRecordResult TradeExists(string tr_id)
        {
            TransactionRecordResult res;

            //check in positions...
            foreach (string pos_key in _positions.Keys)
            {
                PositionIDRecord pidr = new PositionIDRecord(_positions[pos_key]);

                res = pidr.ContainsId(tr_id);
                if (res.MatchType != TransactionMatchType.None)
                {
                    return res;
                }
            }

            return new TransactionRecordResult();
        }
        public bool PositionExists(string pos_id)
        {//pos id may be any type of position order...break it into an ID
            IDString ids = new IDString(pos_id);
            return (_positions.ContainsKey(ids.Num.ToString()));
        }

        public PositionFetchResult FetchPosition(string p)
        {
            PositionFetchResult res = new PositionFetchResult();
            if (!PositionExists(p))
            {
                res.setError("No position found for id '" + p + "'");
                return res;
            }
            IDString pid = new IDString(p);
            res.PositionId = pid.Num.ToString();
            res.ResultObject = _positions[res.PositionId];
            res.SymbolName = SymbolID;
            return res;
        }

        public TransactionFetchResult FetchTransaction(int trans_num, int link_num)
        {
            TransactionFetchResult res = new TransactionFetchResult();

            foreach (string pos_key in _positions.Keys)
            {
                PositionIDRecord pidr = new PositionIDRecord(_positions[pos_key]);

                TransactionRecordResult tres = pidr.ContainsTrans(trans_num, link_num);
                if (tres.MatchType != TransactionMatchType.None)
                {
                    res.IsLinked = tres.IsLinked;
                    res.PositionId = tres.PositionId;
                    res.OrderId = tres.OrderId;
                    res.SymbolName = SymbolID;

                    res.ResultObject = _positions[pos_key];

                    if (!_positions.ContainsKey(pos_key))
                    {
                        res.setError("No position record found for '" + pos_key + "'.");
                        return res;
                    }

                    if (!_positions[pos_key].TradeRecords.ContainsKey(res.OrderId))
                    {
                        res.setError("Trade record (oid='" + res.OrderId + "') not found in position '" + res.PositionId + "' for transaction record.");
                        return res;
                    }

                    //for later convienience, store a reference to the trade record
                    res.TransactionTradeRecord = _positions[pos_key].TradeRecords[res.OrderId];

                    return (res);
                }
            }
            res.setError("No position matched trans='" + trans_num + "' link='" + link_num + "'");
            return res;
        }

        /*
        public PositionFetchResult popBrokerPositionRecord(string bpr_id)
        {
            PositionFetchResult res = new PositionFetchResult();


                // <-- position is not in use and lock is held

                if (! _positions.ContainsKey(bpr_id))
                {
                    res.setError("No position record found for '" + bpr_id + "'.");
                    return res;
                }


                //remove it from the dictionary
                res.ResultObject = _positions[bpr_id];

                res.PositionId = bpr_id;
                if (! _positions.Remove(bpr_id) )
                {
                    res.setError("Unable to remove position record '" + bpr_id + "' from table.");
                    return res;
                }
                return (res);
        }
        */

        public FunctionResult pushBrokerPositionRecord(BrokerPositionRecord bpr)
        {
            FunctionResult res = new FunctionResult();
            _positions[bpr.ID] = bpr;
            return res;
        }


        public int getTotalSize()
        {//sum of all open filled market/limit sizes
            int n = 0;
            foreach (string bpr_key in _positions.Keys)
            {
                BrokerPositionRecord bpr = _positions[bpr_key];
                foreach (IDString tr_key in bpr.TradeRecords.Keys)
                {
                    TradeRecord tr = bpr.TradeRecords[tr_key];

                    BrokerOrder trbo = tr.openOrder.BrokerOrder;
                    if (trbo.OrderState == BrokerOrderState.Filled || trbo.OrderState == BrokerOrderState.PartiallyFilled)
                    {
                        n += (int)trbo.Shares;
                    }
                }
            }
            return n;
        }
    }

    [Serializable]
    public class BrokerSymbolRecords : RightEdgeOandaPlugin.SerializableDictionary<string, BrokerPositionRecords>
    {
        public BrokerSymbolRecords(int id) { _id = id; }
        public BrokerSymbolRecords() { }

        private int _id;
        public int AccountID { set { _id = value; } get { return (_id); } }
    }
    #endregion

    [Serializable]
    public class OrderBookData : XMLFileSerializeBase
    {
        public OrderBookData() { }
        private RightEdgeOandaPlugin.SerializableDictionary<int, BrokerSymbolRecords> _accounts = new RightEdgeOandaPlugin.SerializableDictionary<int, BrokerSymbolRecords>();
        public RightEdgeOandaPlugin.SerializableDictionary<int, BrokerSymbolRecords> Accounts { set { _accounts = value; } get { return (_accounts); } }

        public void LogData(PluginLog log)
        {
            log.captureDebug("<--- Begin Order Book Data Dump");

            log.captureDebug("Book contains '" + _accounts.Count + "' accounts");
            foreach (int act_id in _accounts.Keys)
            {
                BrokerSymbolRecords bsr= _accounts[act_id];
                log.captureDebug(" Account '" + act_id + "' contains '" + bsr.Count + "' symbols");

                foreach (string sym in bsr.Keys)
                {
                    BrokerPositionRecords bprl = bsr[sym];
                    log.captureDebug("  Account '" + act_id + "'/Symbol '" + sym + "' contains '" + bprl.Positions.Count + "' positions");

                    foreach (string posid in bprl.Positions.Keys)
                    {
                        BrokerPositionRecord bpr = bprl.Positions[posid];
                        log.captureDebug("   Position '" + posid + "'/Account '" + act_id + "'/Symbol '" + sym + "' contains '" + bpr.TradeRecords.Count + "' trade records");

                        foreach (IDString trid in bpr.TradeRecords.Keys)
                        {
                            TradeRecord tr = bpr.TradeRecords[trid];
                            log.captureDebug("    Trade Record id='" + tr.OrderID.ID + "' order type='" + tr.openOrder.OrderType + "' trans type='" + tr.openOrder.TransactionType + "' state='" + tr.openOrder.BrokerOrderState + "'");
                        }
                    }
                }
            }
            log.captureDebug("<--- End Order Book Data Dump");
        }

        public void ClearREOwned()
        {
            foreach(int a in _accounts.Keys)
            {
                BrokerSymbolRecords bsrl = _accounts[a];
                foreach(string s in bsrl.Keys)
                {
                    BrokerPositionRecords bprl = bsrl[s];
                    foreach (string p in bprl.Positions.Keys)
                    {
                        bprl.Positions[p].ClearREOwned();
                    }
                }
            }
        }

        public FunctionResult VerifyREOwned()
        {
            FunctionResult fres=new FunctionResult();
            foreach (int a in _accounts.Keys)
            {
                BrokerSymbolRecords bsrl = _accounts[a];
                foreach (string s in bsrl.Keys)
                {
                    BrokerPositionRecords bprl = bsrl[s];
                    foreach (string p in bprl.Positions.Keys)
                    {
                        fres = bprl.Positions[p].VerifyREOwned();
                        if (fres.Error) { return fres; }
                    }
                }
            }

            return fres;
        }

        public FunctionResult SetBrokerOrder(BrokerOrder o)
        {
            FunctionResult fres = new FunctionResult();
            bool found = false;
            foreach (int a in _accounts.Keys)
            {
                if (found) { break; }
                BrokerSymbolRecords bsrl = _accounts[a];

                if (!bsrl.ContainsKey(o.OrderSymbol.Name))
                {//no position record list found for the symbol
                    continue;
                }

                BrokerPositionRecords bprl = bsrl[o.OrderSymbol.Name];

                foreach (string p in bprl.Positions.Keys)
                {
                    if (found) { break; }

                    BrokerPositionRecord bpr = bprl.Positions[p];
                    if (bpr.ID == o.PositionID)
                    {
                        if (bpr.CloseOrder != null && bpr.CloseOrder.OrderID == o.OrderId)
                        {//matches p.close
                            bpr.CloseOrder.BrokerOrder = o;
                            bpr.CloseOrder.IsRightEdgeOrder = true;
                            found = true;
                            break;
                        }
                        else if (bpr.StopOrder != null && bpr.StopOrder.OrderID == o.OrderId)
                        {//matches p.stop
                            bpr.StopOrder.BrokerOrder = o;
                            bpr.StopOrder.IsRightEdgeOrder = true;
                            found = true;
                            break;
                        }
                        else if (bpr.TargetOrder != null && bpr.TargetOrder.OrderID == o.OrderId)
                        {//matches p.target
                            bpr.TargetOrder.BrokerOrder = o;
                            bpr.TargetOrder.IsRightEdgeOrder = true;
                            found = true;
                            break;
                        }
                        else
                        {//search the trade records
                            foreach (IDString trk in bpr.TradeRecords.Keys)
                            {
                                if (found) { break; }

                                TradeRecord tr = bpr.TradeRecords[trk];
                                if (tr.openOrder != null && tr.openOrder.OrderID == o.OrderId)
                                {//matches p.close
                                    tr.openOrder.BrokerOrder = o;
                                    tr.openOrder.IsRightEdgeOrder = true;
                                    found = true;
                                    break;
                                }
                                if (tr.closeOrder != null && tr.closeOrder.OrderID == o.OrderId)
                                {//matches p.close
                                    tr.closeOrder.BrokerOrder = o;
                                    tr.closeOrder.IsRightEdgeOrder = true;
                                    found = true;
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            if (!found)
            { fres.setError("Unable to set broker order, no matching order found in the orderbook"); }
            return fres;
        }

        public override FunctionResult initFromSerialized<T>(T src)
        {
            if (typeof(T) == typeof(OrderBookData))
            { _accounts = ((OrderBookData)(object)src).Accounts; }
            return base.initFromSerialized<T>(src);
        }
    }

    [Synchronization(SynchronizationAttribute.REQUIRED)]
    public class OrderBook : ContextBoundObject
    {
        public OrderBook()
        {
        }
        public OrderBook(OAPluginOptions opts)
        {
            setOptions(opts);
        }

        #region internal objects
        private OAPluginOptions _opts = null;
        public OAPluginOptions OAPluginOptions { set { setOptions(value); } get { return (_opts); } }

        //private string _ename = "broker";
        private TradeEntities _trade_entities = null;
        public TradeEntities TradeEntities { get { return (_trade_entities); } }

        private AccountValuesStore _account_values = null;
        public AccountValuesStore AccountValues { get { return (_account_values); } }
        public bool HaveAccountValues { get { return (_account_values != null && ((_opts != null && _opts.AccountValuesEnabled) || _opts == null)); } }

        private OandAPlugin _parent = null;
        public OandAPlugin OAPlugin { set { _parent = value; } get { return (_parent); } }

        private BrokerLog _log = null;
        public BrokerLog BrokerLog { set { _log = value; } get { return (_log); } }

        private FillQueue _fill_queue = new FillQueue();
        private OrderBookData _accounts = new OrderBookData();
        #endregion

        private void setOptions(OAPluginOptions opts)
        {
            _opts = opts;
            if (_opts == null) { _trade_entities = null; _account_values = null; return; }

            try
            {
                _trade_entities = null;
                FunctionObjectResult<TradeEntities> tesres = TradeEntities.newFromSettings<TradeEntities>(_opts.TradeEntityFileName);
                if (!tesres.Error)
                {
                    _trade_entities = tesres.ResultObject;
                }
            }
            catch
            {
                _trade_entities = null;
            }

            if (_trade_entities == null)
            {
                _trade_entities = new TradeEntities(_opts.DefaultAccount, _opts.DefaultUpperBoundsType, _opts.DefaultUpperBoundsValue, _opts.DefaultLowerBoundsType, _opts.DefaultLowerBoundsValue);
                _trade_entities.FileName = _opts.TradeEntityFileName;
            }
            
            _trade_entities.RefreshOnLookup = true;

            if (_opts.AccountValuesEnabled)
            {
                try
                {
                    _account_values = null;
                    FunctionObjectResult<AccountValuesStore> avsres = AccountValuesStore.newFromSettings<AccountValuesStore>(_opts.AccountValuesFileName);
                    if (!avsres.Error)
                    {
                        _account_values = avsres.ResultObject;
                    }
                }
                catch(Exception)
                {
                    _account_values = null;
                }

                if (_account_values == null)
                {
                    _account_values = new AccountValuesStore();
                    _account_values.FileName = _opts.AccountValuesFileName;
                    try
                    {
                        _account_values.saveSettings<AccountValuesStore>();
                    }
                    catch(Exception)
                    {
                        _account_values = null;
                        return;
                    }
                }
            }
            else
            {
                _account_values = null;
            }
            return;
        }

        #region transaction / position lookup
        private TransactionFetchResult fetchBrokerPositionRecordByTransResponse(ResponseRecord response)
        {
            TransactionFetchResult fetch_ret = new TransactionFetchResult();

            //find the bprl for response acct/sym
            int act_id = response.AccountId;
            string sym_id = response.Transaction.Base + "/" + response.Transaction.Quote;

            if (!_accounts.Accounts.ContainsKey(act_id))
            {
                fetch_ret.setError("unable to locate orderbook symbol page for account id '" + act_id.ToString() + "'");
                return fetch_ret;
            }

            BrokerSymbolRecords bsrl = _accounts.Accounts[act_id];

            if (!bsrl.ContainsKey(sym_id))
            {
                fetch_ret.setError("unable to locate orderbook position page for account id '" + act_id.ToString() + "' / symbol '" + sym_id + "'");
                return fetch_ret;
            }

            BrokerPositionRecords bprl = bsrl[sym_id];

            fetch_ret = bprl.FetchTransaction(response.Transaction.TransactionNumber, response.Transaction.Link);
            fetch_ret.AccountId = act_id;
            return fetch_ret;
        }
        private PositionFetchResult fetchBrokerPositionRecordByTradeID(string tr_id)
        {//try to find the bpr by a trade record id string
            PositionFetchResult ret = new PositionFetchResult();

            //there's no way to know where this tr_id is in the account/symbol/position pages, so search them...
            foreach (int account_id in _accounts.Accounts.Keys)
            {
                BrokerSymbolRecords bsrl = _accounts.Accounts[account_id];
                foreach (string sym_id in bsrl.Keys)
                {
                    BrokerPositionRecords bprl = bsrl[sym_id];
                    TransactionRecordResult tr_res;

                    tr_res = bprl.TradeExists(tr_id);

                    if (tr_res.Error)
                    {
                        ret.setError(tr_res.Message);
                        return ret;
                    }

                    if (!tr_res.PositionExists) { continue; }

                    PositionFetchResult fetch_ret = bprl.FetchPosition(tr_res.PositionId);
                    fetch_ret.AccountId = account_id;
                    return fetch_ret;

                }
            }
            ret.setError("position record not found for trade id '" + tr_id + "'");
            return ret;
        }
        
        private PositionFetchResult fetchBrokerPositionRecord(string pos_id)
        {//try to find the bpr by a position id string (may be any type of position order (open/stop/target/close)
            //there's no way to know where this pos_id is in the account/symbol pages, so search them...
            foreach (int account_id in _accounts.Accounts.Keys)
            {
                BrokerSymbolRecords bsrl = _accounts.Accounts[account_id];
                foreach (string sym_id in bsrl.Keys)
                {
                    BrokerPositionRecords bprl = bsrl[sym_id];

                    if (!bprl.PositionExists(pos_id)) { continue; }

                    PositionFetchResult fetch_ret = bprl.FetchPosition(pos_id);
                    fetch_ret.AccountId = account_id;
                    return fetch_ret;
                }
            }

            PositionFetchResult ret = new PositionFetchResult();
            ret.setError("position record not found for position id '" + pos_id + "'");
            return ret;
        }
        private PositionFetchResult fetchBrokerPositionRecord(string sym_id, string pos_id)
        {
            PositionFetchResult ret = new PositionFetchResult();
            foreach (int act_id in _accounts.Accounts.Keys)
            {
                BrokerSymbolRecords bsrl = _accounts.Accounts[act_id];

                if (!bsrl.ContainsKey(sym_id))
                {
                    continue;
                }

                BrokerPositionRecords bprl = bsrl[sym_id];
                if (!bprl.PositionExists(pos_id))
                {
                    continue;
                }

                ret = bprl.FetchPosition(pos_id);
                ret.AccountId = act_id;
                return ret;
            }

            ret.setError("position record not found for position id '" + pos_id + "'");
            return ret;
        }

        private TransactionFetchResult fetchBrokerPositionRecordByBestFit(BrokerOrder order)
        {//this order is not able to be found by pos id...
            TransactionFetchResult ret = new TransactionFetchResult();

            if (order.TransactionType != TransactionType.Sell && order.TransactionType != TransactionType.Cover)
            {
                ret.setError("Only close requests can be matched by best fit.");
                return ret;
            }

            string sym_id = order.OrderSymbol.Name;
            foreach (int act_id in _accounts.Accounts.Keys)
            {
                BrokerSymbolRecords bsrl = _accounts.Accounts[act_id];
                if (!bsrl.ContainsKey(sym_id))
                {
                    continue;
                }

                BrokerPositionRecords bprl = bsrl[sym_id];

                //find the first open CancelToClose order and return that position
                foreach (string bpr_key in bprl.Positions.Keys)
                {
                    BrokerPositionRecord bpr = bprl.Positions[bpr_key];

                    foreach (IDString tr_key in bpr.TradeRecords.Keys)
                    {
                        TradeRecord tr = bpr.TradeRecords[tr_key];

                        if (!tr.openOrder.CancelToClose) { continue; }

                        //cancel to close open order...does it match the close request order?

                        if (order.Shares == tr.openOrder.BrokerOrder.Shares)
                        {//at this point the acct/symbol/shares all match...call it a fit...
                            ret.ResultObject = bpr;
                            ret.PositionId = tr.openOrder.BrokerOrder.PositionID;
                            ret.TransactionTradeRecord = tr;
                            ret.OrderId = tr.OrderID;
                            ret.AccountId = act_id;
                            return ret;
                        }
                    }
                }
            }
            ret.setError("No open order found which fits this close request.");
            return ret;
        }
        #endregion


        private FunctionResult pushTradeRecord(int act_id, BrokerOrder open_order, bool is_re)
        {
            FunctionResult res = new FunctionResult();
            BrokerPositionRecords bprl;
            PositionFetchResult fetch_bpr = new PositionFetchResult();

            fetch_bpr.AccountId = act_id;
            fetch_bpr.SymbolName = open_order.OrderSymbol.Name;

            if (!_accounts.Accounts.ContainsKey(act_id))
            {
                _accounts.Accounts[act_id] = new BrokerSymbolRecords(act_id);
            }

            BrokerSymbolRecords bsrl = _accounts.Accounts[act_id];

            string sym = fetch_bpr.SymbolName;

            if (!bsrl.ContainsKey(sym))
            {
                bsrl[sym] = new BrokerPositionRecords(sym);
            }

            bprl = bsrl[sym];

            fetch_bpr = bprl.FetchPosition(open_order.PositionID);

            if (!fetch_bpr.PositionExists)
            {
                BrokerPositionRecord bpr = new BrokerPositionRecord(open_order.PositionID);
                bpr.ID = open_order.PositionID;
                bpr.Symbol = open_order.OrderSymbol;
                bpr.Direction = (open_order.TransactionType == TransactionType.Short) ? PositionType.Short : PositionType.Long;

                bprl.Positions.Add(bpr.ID, bpr);

                fetch_bpr.ResultObject = bpr;
            }

            return fetch_bpr.ResultObject.pushTrade(open_order, is_re);
        }

        public FXClientTaskObjectResult<AccountResult> AccountResolution(string entity_id)
        {
            return AccountResolution(entity_id, true);
        }
        public FXClientTaskObjectResult<AccountResult> AccountResolution(string entity_id, bool activate_responder)
        {
            FXClientTaskObjectResult<AccountResult> res = new FXClientTaskObjectResult<AccountResult>();
            string act_id = _trade_entities.GetAccount(entity_id);
            
            //if (string.IsNullOrEmpty(act_id))
            //{
            //    res.setError("unable to get an account identifier for entity '" + entity_id + "'", FXClientResponseType.Invalid, false);
            //    return res;
            //}

            FXClientObjectResult<AccountResult> ares = _parent.fxClient.ConvertStringToAccount(act_id);
            if (ares.Error)
            {
                res.setError(ares.Message, ares.FXClientResponse, ares.Disconnected);
                return res;
            }
            res.ResultObject = ares.ResultObject;

            if (!activate_responder) { return res; }

            Account acct = res.ResultObject.FromInChannel;
            FXClientTaskResult tres = _parent.ResponseProcessor.ActivateAccountResponder(acct);
            res.TaskCompleted = tres.TaskCompleted;
            if (tres.Error)
            {
                res.setError(tres.Message, tres.FXClientResponse, tres.Disconnected);
                return res;
            }
            return res;
        }

        #region order submission
        public FXClientTaskResult SubmitLimitOrder(BrokerOrder order, out string act_id)
        {
            FXClientTaskResult res = new FXClientTaskResult();
            string entity_id = TradeEntityID.CreateID(_opts.TradeEntityName, order);

            FXClientTaskObjectResult<AccountResult> tares = AccountResolution(entity_id);
            res.TaskCompleted = tares.TaskCompleted;
            if(tares.Error)
            {
                res.setError(tares.Message, tares.FXClientResponse, tares.Disconnected);
                act_id = string.Empty;
                return res;
            }
            Account acct = tares.ResultObject.FromInChannel;
            act_id = acct.AccountId.ToString();

            fxPair oa_pair = new fxPair(order.OrderSymbol.ToString());
            LimitOrder lo = new LimitOrder();

            lo.Base = oa_pair.Base;
            lo.Quote = oa_pair.Quote;

            lo.Units = (int)order.Shares;
            if (order.TransactionType == TransactionType.Short)
            { lo.Units = -1 * lo.Units; }

            double n=_trade_entities.GetUpperBoundsPrice(entity_id, order.LimitPrice);
            lo.HighPriceLimit = ((n == order.LimitPrice) ? 0.0 : n);
                
            n= _trade_entities.GetLowerBoundsPrice(entity_id, order.LimitPrice);
            lo.LowPriceLimit = ((n == order.LimitPrice) ? 0.0 : n);

            lo.Price = order.LimitPrice;

            int h = 36;//FIX ME - how many hours should a limit order last???
            if (h != 0)
            {
                DateTime duration = new DateTime(DateTime.UtcNow.Ticks);
                duration = duration.AddHours(h);
                lo.Duration = duration;
            }
            //else - the default limit order duration is 1 hour

            Thread.BeginCriticalRegion();//FIX ME - the thread is losing control when calling the fxclient wrapper
            //FIX ME - this results in another thread grbbing the order book and looking for an order which isn't there yet....
            //FIX ME - will this critical region stop that from happening??

            FXClientResult sres = _parent.fxClient.SendOAExecute(acct, lo);
            if (sres.Error) { res.setError(sres.Message, sres.FXClientResponse, sres.Disconnected); Thread.EndCriticalRegion(); return res; }

            order.OrderState = BrokerOrderState.Submitted;
            order.OrderId = lo.Id.ToString();

            FunctionResult fres = pushTradeRecord(acct.AccountId, order, true);
            if (fres.Error) { res.setError(fres.Message); }

            Thread.EndCriticalRegion();
            return res;
        }
        public FXClientTaskResult SubmitMarketOrder(BrokerOrder order, out string act_id)
        {
            FXClientTaskResult res = new FXClientTaskResult();
            string entity_id = TradeEntityID.CreateID(_opts.TradeEntityName, order);

            FXClientTaskObjectResult<AccountResult> tares = AccountResolution(entity_id);
            res.TaskCompleted = tares.TaskCompleted;
            if (tares.Error)
            {
                res.setError(tares.Message, tares.FXClientResponse, tares.Disconnected);
                act_id = string.Empty;
                return res;
            }
            Account acct = tares.ResultObject.FromInChannel;
            act_id = acct.AccountId.ToString();

            fxPair oa_pair = new fxPair(order.OrderSymbol.ToString());
            MarketOrder mo = new MarketOrder();

            mo.Base = oa_pair.Base;
            mo.Quote = oa_pair.Quote;

            mo.Units = (int)order.Shares;
            if (order.TransactionType == TransactionType.Short)
            { mo.Units = -1 * mo.Units; }

            double n = _trade_entities.GetUpperBoundsPrice(entity_id, order.LimitPrice);
            mo.HighPriceLimit = n == order.LimitPrice ? 0.0 : n;
            
            n = _trade_entities.GetLowerBoundsPrice(entity_id, order.LimitPrice);
            mo.LowPriceLimit = n == order.LimitPrice ? 0.0 : n;

            FXClientResult fxcres = _parent.fxClient.SendOAExecute(acct, mo);
            if (fxcres.Error)
            {
                res.setError(fxcres.Message, fxcres.FXClientResponse, fxcres.Disconnected);
                return res;
            }

            order.OrderState = BrokerOrderState.Submitted;
            order.OrderId = mo.Id.ToString();

            FunctionResult fres = pushTradeRecord(acct.AccountId, order, true);
            if (fres.Error) { res.setError(fres.Message); }
            return res;
        }

        public FXClientTaskResult SubmitCloseOrder(BrokerOrder order, out string act_id)
        {
            act_id = string.Empty;
            FXClientTaskResult res = new FXClientTaskResult();
            PositionFetchResult fetch_bpr = null;
            int orders_sent = 0;
            BrokerPositionRecord cp = null;
            try
            {
                fetch_bpr = fetchBrokerPositionRecord(order.OrderSymbol.ToString(), order.PositionID);
                if (fetch_bpr.Error)
                {
                    //since their is no orderID in the order and PositionID is not valid
                    //have to do "best fit" matching.... ughhh...
                    TransactionFetchResult tfetch_bpr = fetchBrokerPositionRecordByBestFit(order);
                    if (tfetch_bpr.Error)
                    {//still did not find it, return the original error and the best fit error
                        res.setError("Unable to locate close order's position record : '" + fetch_bpr.Message + "'. " + tfetch_bpr.Message, FXClientResponseType.Rejected, false);
                        return res;
                    }

                    //found the position by the trade id...
                    BrokerPositionRecord tbpr = tfetch_bpr.ResultObject;
                    TradeRecord tr = tfetch_bpr.TransactionTradeRecord;

                    if (!tr.openOrder.CancelToClose)
                    {//whoah nelly, this is a bad thing....
                        res.setError("Found close order's trade record by order id, but it's not a 'cancel to close'", FXClientResponseType.Rejected, false);
                        return res;
                    }

                    //remove the trade record from this position.
                    if (!tbpr.TradeRecords.Remove(tr.OrderID))
                    {
                        res.setError("Unable to remove trade record from position.", FXClientResponseType.Rejected, false);
                        return res;
                    }

                    //create a new position and add the trade record to the new position
                    tr.openOrder.BrokerOrder.PositionID = order.PositionID;//set the new position ID
                    FunctionResult fr = pushTradeRecord(tfetch_bpr.AccountId, tr.openOrder.BrokerOrder, true);
                    if (fr.Error)
                    {
                        res.setError("Unable to push new position for cancel to close. " + fr.Message, FXClientResponseType.Rejected, false);
                        return res;
                    }

                    //now fetch the new position and
                    fetch_bpr = fetchBrokerPositionRecord(order.OrderSymbol.ToString(), order.PositionID);
                    if (fetch_bpr.Error)
                    {
                        res.setError("Unable to fetch new position for cancel to close." + fetch_bpr.Message, FXClientResponseType.Rejected, false);
                        return res;
                    }
                    //proceed with the close
                }

                cp = fetch_bpr.ResultObject;

                order.OrderState = BrokerOrderState.Submitted;
                IDString cid = new IDString(IDType.Close, int.Parse(order.PositionID));
                order.OrderId = cid.ID;

                if (cp.CloseOrder != null)
                {
                    res.setError("pre-existing close order id='" + cp.CloseOrder.BrokerOrder.OrderId + "'", FXClientResponseType.Rejected, false);
                    return res;
                }

                cp.CloseOrder = new OrderRecord(order, true);

                bool order_checked = false;
                bool do_cancel = false;


                ////////////////////////////////
                FXClientTaskObjectResult<AccountResult> tares = AccountResolution(TradeEntityID.CreateID(_opts.TradeEntityName, order));
                res.TaskCompleted = tares.TaskCompleted;
                if (tares.Error)
                {
                    res.setError(tares.Message, tares.FXClientResponse, tares.Disconnected);
                    act_id = string.Empty;
                    return res;
                }
                Account acct = tares.ResultObject.FromInChannel;
                act_id = acct.AccountId.ToString();
                ////////////////////////////////

                

                foreach (IDString tr_key in cp.TradeRecords.Keys)
                {
                    TradeRecord tr = cp.TradeRecords[tr_key];
                    MarketOrder cmo = new MarketOrder();

                    FunctionObjectResult<int> idres = tr.IDNumber();
                    if (idres.Error)
                    {
                        res.setError("Unable to process close order : " + idres.Message, FXClientResponseType.Rejected, false);
                        if (orders_sent == 0) { cp.CloseOrder = null; }
                        return res;
                    }
                    int id_num = idres.ResultObject;

                    if (tr.openOrder.StopHit || tr.openOrder.TargetHit)
                    {//this order is already closed...
                        continue;
                    }

                    if (!order_checked) { order_checked = true; }

                    BrokerOrderState ostate = BrokerOrderState.Submitted;
                    FunctionObjectResult<MarketOrder> fores = _parent.fxClient.GetTradeWithID(acct, id_num);

                    if (!fores.Error)
                    {//no error...
                        cmo = fores.ResultObject;
                        FXClientResult subres = _parent.fxClient.SendOAClose(acct, cmo);
                        if (subres.Error)
                        {
                            if (!subres.OrderMissing) { res.setError(subres.Message, subres.FXClientResponse, subres.Disconnected); return res; }
                            res.OrderMissing = true;
                        }
                        orders_sent++;
                    }
                    else
                    {//GetTradeWithID() error...
                        res.OrderMissing = true;
                    }

                    if (res.OrderMissing)
                    {
                        if (cp.StopOrder != null || cp.TargetOrder != null)
                        {//expecting a target/stop hit event from oanda
                            //so set ostate to pending cancel and don't send anything to oanda
                            ostate = BrokerOrderState.PendingCancel;
                            do_cancel = true;
                        }
                        else
                        {//it's an error if not expecting a target/stop order fill event
                            res.setError("Unable to locate trade id '" + id_num + "' for position '" + cp.ID + "' at oanda.", FXClientResponseType.Rejected, false);
                            if (orders_sent == 0) { cp.CloseOrder = null; }
                            return res;
                        }
                    }


                    BrokerOrder bro = new BrokerOrder();
                    IDString id_s = new IDString(IDType.Close, id_num);
                    bro.Shares = (ostate == BrokerOrderState.PendingCancel) ? 0 : (long)cmo.Units;
                    bro.OrderState = ostate;
                    bro.OrderId = id_s.ID;
                    bro.SubmittedDate = DateTime.Now;
                    bro.PositionID = order.PositionID;
                    bro.OrderSymbol = order.OrderSymbol;
                    bro.OrderType = order.OrderType;
                    bro.TransactionType = order.TransactionType;

                    tr.closeOrder = new OrderRecord(bro, false);

                    if (do_cancel) { break; }
                }

                if (orders_sent == 0)
                {
                    _log.captureDebug("close request found no orders to send, canceling it.");
                    do_cancel = true;
                }

                if (do_cancel)
                {
                    //move the position close order into pending cancel
                    cp.CloseOrder.BrokerOrder.OrderState = BrokerOrderState.PendingCancel;
                    _parent.FireOrderUpdated(cp.CloseOrder.BrokerOrder, null, "canceling close request");

                    if (!order_checked)
                    {//all orders in the position have had a target/stop hit event...finalize the close now
                        BrokerOrder cbo = cp.CloseOrder.BrokerOrder;
                        cbo.OrderState = BrokerOrderState.Cancelled;
                        cp.CloseOrder = null;
                        _parent.FireOrderUpdated(cbo, null, "close canceled on empty position");
                    }
                }

                res.FXClientResponse = FXClientResponseType.Accepted;
                return res;
            }
            catch (Exception e)
            {
                _log.captureException(e);
                res.setError("Unhandled exception while closing orders. : " + e.Message, FXClientResponseType.Rejected, false);
                if (cp != null && orders_sent == 0) { cp.CloseOrder = null; }
                return res;
            }
        }

        public FXClientTaskResult SubmitPositionStopLossOrder(BrokerOrder order, out string act_id)
        {
            act_id = string.Empty;
            FXClientTaskResult res = new FXClientTaskResult();
            PositionFetchResult fetch_bpr = null;
            try
            {
                fetch_bpr = fetchBrokerPositionRecord(order.OrderSymbol.ToString(), order.PositionID);
                if (fetch_bpr.Error)
                {
                    res.setError("Unable to locate stop order's position record : '" + fetch_bpr.Message + "'.", FXClientResponseType.Rejected, false);
                    return res;
                }

                BrokerPositionRecord bpr = fetch_bpr.ResultObject;

                ////////////////////////////////
                FXClientTaskObjectResult<AccountResult> tares = AccountResolution(TradeEntityID.CreateID(_opts.TradeEntityName, order));
                res.TaskCompleted = tares.TaskCompleted;
                if (tares.Error)
                {
                    res.setError(tares.Message, tares.FXClientResponse, tares.Disconnected);
                    act_id = string.Empty;
                    return res;
                }
                Account acct = tares.ResultObject.FromInChannel;
                act_id = acct.AccountId.ToString();
                ////////////////////////////////


                IDString n_id = new IDString(IDType.Stop, int.Parse(order.PositionID), bpr.StopNumber++);
                order.OrderId = n_id.ID;

                FXClientResult fxcres = SubmitStopOrders(bpr, order, acct);
                if (fxcres.Error)
                { res.setError(fxcres.Message, fxcres.FXClientResponse, fxcres.Disconnected); }
                return res;
            }
            catch (Exception e)
            {
                res.setError("Unhandled exception : '" + e.Message + "'.", FXClientResponseType.Rejected, false);
                return res;
            }
        }
        private FXClientResult SubmitStopOrders(BrokerPositionRecord bpr, BrokerOrder order, Account acct)
        {
            FXClientResult res = new FXClientResult();
            int orders_sent = 0;
            int act_id = acct.AccountId;
            double stop_price = order.StopPrice;

            bool stop_added = false;
            if (bpr.StopOrder == null)
            {
                stop_added = true;
                bpr.StopOrder = new OrderRecord(order, true);
            }
            else if (order.StopPrice != 0.0)
            {
                res.setError("pstop order already exists!", FXClientResponseType.Rejected, false);
                return res;
            }

            #region send stop orders
            //for each traderecord set the openorder stop price at oanda

            foreach (IDString tr_key in bpr.TradeRecords.Keys)
            {
                TradeRecord tr = bpr.TradeRecords[tr_key];

                FunctionObjectResult<int> idres = tr.IDNumber();
                if (idres.Error)
                {
                    if (stop_added && orders_sent == 0) { bpr.TargetOrder = null; }
                    res.setError("Unable to modify stop : " + idres.Message, FXClientResponseType.Rejected, false);
                    res.OrdersSent = orders_sent;
                    return res;
                }
                int id_num = idres.ResultObject;

                if (tr.openOrder.BrokerOrder.OrderType == OrderType.Limit && tr.openOrder.BrokerOrder.OrderState == BrokerOrderState.Submitted)
                {
                    #region modify pending orders
                    LimitOrder lo;
                    FXClientObjectResult<LimitOrder> fores = _parent.fxClient.GetOrderWithID(acct, id_num);
                    if (fores.Error)
                    {
                        if (fores.Disconnected)
                        {
                            if (stop_added && orders_sent == 0) { bpr.StopOrder = null; }
                            res.setError(fores.Message, fores.FXClientResponse, fores.Disconnected);
                            res.OrdersSent = orders_sent;
                            return res;
                        }

                        if (stop_price == 0.0)
                        {
                            _log.captureDebug("Stop Cancel Request : Unable to locate oanda limit order for pending order '" + id_num + "', but allowing cancel anyway.");
                            orders_sent++;//"pretend" and order was sent to trigger the handler and leave the order in PendingCancel
                            continue;
                        }
                        if (stop_added && orders_sent == 0) { bpr.StopOrder = null; }

                        res.setError("Stop Modify Request : Unable to locate oanda limit order for pending order '" + id_num + "'.", FXClientResponseType.Rejected, false);
                        res.OrdersSent = orders_sent;
                        return res;
                    }

                    lo = fores.ResultObject;

                    if (lo.stopLossOrder.Price == stop_price) { continue; }
                    lo.stopLossOrder.Price = stop_price;

                    res = _parent.fxClient.SendOAModify(acct, lo);

                    #region modify error test
                    if (res.Error)
                    {
                        if (res.Disconnected)
                        {
                            if (stop_added && orders_sent == 0) { bpr.StopOrder = null; }
                            res.OrdersSent = orders_sent;
                            return res;
                        }

                        //get a fresh account object (if the output channel was lost, the previous one will be invalid)
                        FXClientTaskObjectResult<AccountResult> acres = AccountResolution(TradeEntityID.CreateID(_opts.TradeEntityName, order));
                        if (acres.Error)
                        {
                            if (stop_added && orders_sent == 0) { bpr.StopOrder = null; }
                            res.setError(acres.Message, acres.FXClientResponse, acres.Disconnected);
                            res.OrdersSent = orders_sent;
                            return res;
                        }
                        acct = acres.ResultObject.FromOutChannel;

                        //check again if the order is missing...
                        FXClientObjectResult<LimitOrder> fores2 = _parent.fxClient.GetOrderWithID(acct, id_num);
                        if (fores2.Error)
                        {//ok, the modify order failed because the order is now missing...
                            if (fores2.Disconnected)
                            {
                                if (stop_added && orders_sent == 0) { bpr.StopOrder = null; }
                                res.setError(fores2.Message, fores2.FXClientResponse, fores2.Disconnected);
                                res.OrdersSent = orders_sent;
                                return res;
                            }

                            if (stop_price == 0.0)
                            {
                                _log.captureDebug("Stop Cancel Request : Unable to locate oanda limit order for pending order '" + id_num + "', but allowing cancel anyway.");
                                orders_sent++;//"pretend" and order was sent to trigger the handler and leave the order in PendingCancel
                                continue;
                            }
                            res.setError("Stop Modify Request : Unable to locate oanda limit order for pending order '" + id_num + "'.", FXClientResponseType.Rejected, false);
                        }
                        if (stop_added && orders_sent == 0) { bpr.StopOrder = null; }

                        res.OrdersSent = orders_sent;
                        return res;
                    }
                    #endregion
                    orders_sent++;
                    #endregion
                }
                else if (tr.openOrder.BrokerOrder.OrderState == BrokerOrderState.Filled)
                {
                    #region modify active orders
                    if (tr.openOrder.TargetHit || tr.openOrder.StopHit)
                    {//this order has been closed out by a hit event...
                        continue;//so skip it...
                    }

                    MarketOrder mo;
                    FXClientObjectResult<MarketOrder> fores = _parent.fxClient.GetTradeWithID(acct, id_num);
                    if (fores.Error)
                    {
                        if (fores.Disconnected)
                        {
                            if (stop_added && orders_sent == 0) { bpr.StopOrder = null; }
                            res.setError(fores.Message, fores.FXClientResponse, fores.Disconnected);
                            res.OrdersSent = orders_sent;
                            return res;
                        }
                        if (stop_price == 0.0)
                        {
                            _log.captureDebug("Stop Cancel Request : Unable to locate oanda trade (market order) for filled order '" + id_num + "', but allowing cancel anyway.");
                            orders_sent++;//"pretend" and order was sent to trigger the handler and leave the order in PendingCancel
                            continue;
                        }
                        if (stop_added && orders_sent == 0) { bpr.StopOrder = null; }

                        res.setError("Stop Modify Request : Unable to locate oanda trade (market order) for filled order '" + id_num + "'.", FXClientResponseType.Rejected, false);
                        res.OrdersSent = orders_sent;
                        return res;
                    }

                    mo = fores.ResultObject;

                    if (mo.stopLossOrder.Price == stop_price) { continue; }

                    _log.captureDebug("  setting market order '" + mo.Id + "' stop price [orig='" + mo.stopLossOrder.Price + "' new='" + stop_price + "']");
                    mo.stopLossOrder.Price = stop_price;

                    res = _parent.fxClient.SendOAModify(acct, mo);

                    #region modify error test
                    if (res.Error)
                    {
                        if (res.Disconnected)
                        {
                            if (stop_added && orders_sent == 0) { bpr.StopOrder = null; }
                            res.OrdersSent = orders_sent;
                            return res;
                        }

                        //get a fresh account object (if the output channel was lost, the previous one will be invalid)
                        FXClientTaskObjectResult<AccountResult> acres = AccountResolution(TradeEntityID.CreateID(_opts.TradeEntityName, order));
                        if (acres.Error)
                        {
                            if (stop_added && orders_sent == 0) { bpr.StopOrder = null; }
                            res.setError(acres.Message, acres.FXClientResponse, acres.Disconnected);
                            res.OrdersSent = orders_sent;
                            return res;
                        }
                        acct = acres.ResultObject.FromOutChannel;

                        //check again if the order is missing...
                        FXClientObjectResult<MarketOrder> fores2 = _parent.fxClient.GetTradeWithID(acct, id_num);
                        if (fores2.Error)
                        {//ok, the modify order failed because the order is now missing...
                            if (fores2.Disconnected)
                            {
                                if (stop_added && orders_sent == 0) { bpr.StopOrder = null; }
                                res.setError(fores2.Message, fores2.FXClientResponse, fores2.Disconnected);
                                res.OrdersSent = orders_sent;
                                return res;
                            }
                            if (stop_price == 0.0)
                            {
                                _log.captureDebug("Stop Cancel Request : Unable to locate oanda trade (market order) for filled order '" + id_num + "', but allowing cancel anyway.");
                                orders_sent++;//"pretend" and order was sent to trigger the handler and leave the order in PendingCancel
                                continue;
                            }
                            res.setError("Stop Modify Request : Unable to locate oanda trade (market order) for filled order '" + id_num + "'.", FXClientResponseType.Rejected, false);
                        }
                        if (stop_added && orders_sent == 0) { bpr.StopOrder = null; }
                        res.OrdersSent = orders_sent;
                        return res;
                    }
                    #endregion
                    orders_sent++;
                    #endregion
                }
                else if (stop_price != 0.0)
                {//unkown stop order error
                    if (stop_added && orders_sent == 0) { bpr.StopOrder = null; }

                    res.setError("Unknown open order state for stop modification. {id='" + tr.openOrder.BrokerOrder.OrderId + "' posid='" + tr.openOrder.BrokerOrder.PositionID + "' type='" + tr.openOrder.BrokerOrder.OrderType + "' state='" + tr.openOrder.BrokerOrder.OrderState + "'}", FXClientResponseType.Rejected, false);
                    res.OrdersSent = orders_sent;
                    return res;
                }
            }
            #endregion

            res.FXClientResponse = FXClientResponseType.Accepted;
            res.OrdersSent = orders_sent;
            return res;
        }

        public FXClientTaskResult SubmitPositionTargetProfitOrder(BrokerOrder order, out string act_id)
        {
            act_id = string.Empty;
            FXClientTaskResult res = new FXClientTaskResult();
            PositionFetchResult fetch_bpr = null;
            try
            {
                fetch_bpr = fetchBrokerPositionRecord(order.OrderSymbol.ToString(), order.PositionID);
                if (fetch_bpr.Error)
                {
                    res.setError("Unable to locate target order's position record : '" + fetch_bpr.Message + "'.", FXClientResponseType.Rejected, false);
                    return res;
                }

                BrokerPositionRecord bpr = fetch_bpr.ResultObject;


                ////////////////////////////////
                FXClientTaskObjectResult<AccountResult> tares = AccountResolution(TradeEntityID.CreateID(_opts.TradeEntityName, order));
                res.TaskCompleted = tares.TaskCompleted;
                if (tares.Error)
                {
                    res.setError(tares.Message, tares.FXClientResponse, tares.Disconnected);
                    act_id = string.Empty;
                    return res;
                }
                Account acct = tares.ResultObject.FromInChannel;
                act_id = acct.AccountId.ToString();
                ////////////////////////////////

                IDString n_id = new IDString(IDType.Target, int.Parse(order.PositionID), bpr.TargetNumber++);
                order.OrderId = n_id.ID;

                FXClientResult fxcres = SubmitTargetOrders(bpr, order, acct);
                if (fxcres.Error)
                { res.setError(fxcres.Message, fxcres.FXClientResponse, fxcres.Disconnected); }
                return res;
            }
            catch (Exception e)
            {
                res.setError("Unhandled exception : '" + e.Message + "'.", FXClientResponseType.Rejected, false);
                return res;
            }
        }
        private FXClientResult SubmitTargetOrders(BrokerPositionRecord bpr, BrokerOrder order, Account acct)
        {
            FXClientResult res = new FXClientResult();
            int orders_sent = 0;
            double target_price = order.LimitPrice;
            int act_id = acct.AccountId;

            bool target_added = false;
            if (bpr.TargetOrder == null)
            {
                target_added = true;
                bpr.TargetOrder = new OrderRecord(order, true);
            }
            else if (order.LimitPrice != 0.0)
            {
                res.setError("ptarget order already exists!", FXClientResponseType.Rejected, false);
                return res;
            }

            #region send target orders
            //for each traderecord set the openorder stop price at oanda

            foreach (IDString tr_key in bpr.TradeRecords.Keys)
            {
                TradeRecord tr = bpr.TradeRecords[tr_key];

                FunctionObjectResult<int> idres = tr.IDNumber();
                if (idres.Error)
                {
                    if (target_added && orders_sent == 0) { bpr.TargetOrder = null; }
                    res.setError("Unable to modify target : " + idres.Message, FXClientResponseType.Rejected, false);
                    res.OrdersSent = orders_sent;
                    return res;
                }
                int id_num = idres.ResultObject;

                if (tr.openOrder.BrokerOrder.OrderType == OrderType.Limit && tr.openOrder.BrokerOrder.OrderState == BrokerOrderState.Submitted)
                {
                    #region modify pending orders
                    LimitOrder lo;

                    FXClientObjectResult<LimitOrder> fores = _parent.fxClient.GetOrderWithID(acct, id_num);
                    if (fores.Error)
                    {
                        if (fores.Disconnected)
                        {
                            if (target_added && orders_sent == 0) { bpr.TargetOrder = null; }
                            res.setError(fores.Message, fores.FXClientResponse, fores.Disconnected);
                            res.OrdersSent = orders_sent;
                            return res;
                        }
                        if (target_price == 0.0)
                        {
                            _log.captureDebug("Target Cancel Request : Unable to locate oanda limit order for pending order '" + id_num + "', but allowing cancel anyway.");
                            orders_sent++;//"pretend" and order was sent to trigger the handler and leave the order in PendingCancel
                            continue;
                        }

                        if (target_added && orders_sent == 0) { bpr.TargetOrder = null; }
                        res.setError("Target Modify Request : Unable to locate oanda limit order for pending order '" + id_num + "'.", FXClientResponseType.Rejected, false);
                        res.OrdersSent = orders_sent;
                        return res;
                    }

                    lo = fores.ResultObject;

                    if (lo.takeProfitOrder.Price == target_price) { continue; }
                    lo.takeProfitOrder.Price = target_price;

                    res = _parent.fxClient.SendOAModify(acct, lo);

                    #region modify error test
                    if (res.Error)
                    {
                        if (res.Disconnected)
                        {
                            if (target_added && orders_sent == 0) { bpr.TargetOrder = null; }
                            res.OrdersSent = orders_sent;
                            return res;
                        }

                        //gett a fresh account object (if the output channel was lost, the previous one will be invalid)
                        FXClientTaskObjectResult<AccountResult> acres = AccountResolution(TradeEntityID.CreateID(_opts.TradeEntityName, order));
                        if (acres.Error)
                        {
                            if (target_added && orders_sent == 0) { bpr.TargetOrder = null; }
                            res.setError(acres.Message, acres.FXClientResponse, acres.Disconnected);
                            res.OrdersSent = orders_sent;
                            return res;
                        }
                        acct = acres.ResultObject.FromOutChannel;

                        //check again if the order is missing...
                        FXClientObjectResult<LimitOrder> fores2 = _parent.fxClient.GetOrderWithID(acct, id_num);
                        if (fores2.Error)
                        {//ok, the modify order failed because the order is now missing...
                            if (fores2.Disconnected)
                            {
                                if (target_added && orders_sent == 0) { bpr.TargetOrder = null; }
                                res.setError(fores2.Message, fores2.FXClientResponse, fores2.Disconnected);
                                res.OrdersSent = orders_sent;
                                return res;
                            }

                            if (target_price == 0.0)
                            {
                                _log.captureDebug("Target Cancel Request : Unable to locate oanda limit order for pending order '" + id_num + "', but allowing cancel anyway.");
                                orders_sent++;//"pretend" and order was sent to trigger the handler and leave the order in PendingCancel
                                continue;
                            }
                            res.setError("Target Modify Request : Unable to locate oanda limit order for pending order '" + id_num + "'.", FXClientResponseType.Rejected, false);
                        }
                        if (target_added && orders_sent == 0) { bpr.TargetOrder = null; }

                        res.OrdersSent = orders_sent;
                        return res;
                    }
                    #endregion
                    orders_sent++;
                    #endregion
                }
                else if (tr.openOrder.BrokerOrder.OrderState == BrokerOrderState.Filled)
                {
                    #region modify active orders
                    if (tr.openOrder.TargetHit || tr.openOrder.StopHit)
                    {//this order has been closed out by a hit event...
                        continue;//so skip it...
                    }
                    MarketOrder mo;
                    FXClientObjectResult<MarketOrder> fores = _parent.fxClient.GetTradeWithID(acct, id_num);
                    if (fores.Error)
                    {
                        if (fores.Disconnected)
                        {
                            if (target_added && orders_sent == 0) { bpr.TargetOrder = null; }
                            res.setError(fores.Message, fores.FXClientResponse, fores.Disconnected);
                            res.OrdersSent = orders_sent;
                            return res;
                        }
                        if (target_price == 0.0)
                        {
                            _log.captureDebug("Target Cancel Request : Unable to locate oanda trade (market order) for filled order '" + id_num + "', but allowing cancel anyway.");
                            orders_sent++;//"pretend" and order was sent to trigger the handler and leave the order in PendingCancel
                            continue;
                        }

                        if (target_added && orders_sent == 0) { bpr.TargetOrder = null; }
                        res.setError("Target Modify Request : Unable to locate oanda trade (market order) for filled order '" + id_num + "'.", FXClientResponseType.Rejected, false);
                        res.OrdersSent = orders_sent;
                        return res;
                    }

                    mo = fores.ResultObject;

                    if (mo.takeProfitOrder.Price == target_price) { continue; }

                    _log.captureDebug("  setting market order '" + mo.Id + "' target price [orig='" + mo.takeProfitOrder.Price + "' new='" + target_price + "']");
                    mo.takeProfitOrder.Price = target_price;

                    res = _parent.fxClient.SendOAModify(acct, mo);
                    #region modify error test
                    if (res.Error)
                    {
                        if (res.Disconnected)
                        {
                            if (target_added && orders_sent == 0) { bpr.TargetOrder = null; }
                            res.OrdersSent = orders_sent;
                            return res;
                        }

                        //gett a fresh account object (if the output channel was lost, the previous one will be invalid)
                        FXClientTaskObjectResult<AccountResult> acres = AccountResolution(TradeEntityID.CreateID(_opts.TradeEntityName, order));
                        if (acres.Error)
                        {
                            if (target_added && orders_sent == 0) { bpr.TargetOrder = null; }
                            res.setError(acres.Message, acres.FXClientResponse, acres.Disconnected);
                            res.OrdersSent = orders_sent;
                            return res;
                        }
                        acct = acres.ResultObject.FromOutChannel;

                        //check again if the order is missing...
                        FXClientObjectResult<MarketOrder> fores2 = _parent.fxClient.GetTradeWithID(acct, id_num);
                        if (fores2.Error)
                        {//ok, the modify order failed because the order is now missing...
                            if (fores2.Disconnected)
                            {
                                if (target_added && orders_sent == 0) { bpr.TargetOrder = null; }
                                res.setError(fores2.Message, fores2.FXClientResponse, fores2.Disconnected);
                                res.OrdersSent = orders_sent;
                                return res;
                            }

                            if (target_price == 0.0)
                            {
                                _log.captureDebug("Target Cancel Request : Unable to locate oanda trade (market order) for filled order '" + id_num + "', but allowing cancel anyway.");
                                orders_sent++;//"pretend" and order was sent to trigger the handler and leave the order in PendingCancel
                                continue;
                            }
                            res.setError("Target Modify Request : Unable to locate oanda trade (market order) for filled order '" + id_num + "'.", FXClientResponseType.Rejected, false);
                        }
                        if (target_added && orders_sent == 0) { bpr.TargetOrder = null; }

                        res.OrdersSent = orders_sent;
                        return res;
                    }
                    #endregion
                    orders_sent++;
                    #endregion
                }
                else if (target_price != 0.0)
                {//unkown target order error
                    if (target_added && orders_sent == 0) { bpr.TargetOrder = null; }

                    res.setError("Unknown open order state for target modification. {id='" + tr.openOrder.BrokerOrder.OrderId + "' posid='" + tr.openOrder.BrokerOrder.PositionID + "' type='" + tr.openOrder.BrokerOrder.OrderType + "' state='" + tr.openOrder.BrokerOrder.OrderState + "'}", FXClientResponseType.Rejected, false);
                    res.OrdersSent = orders_sent;
                    return res;
                }
                //else target of 0.0 is a cancel
            }
            #endregion

            res.FXClientResponse = FXClientResponseType.Accepted;
            res.OrdersSent = orders_sent;
            return res;
        }
        #endregion

        #region order cancel
        public FXClientResult CancelPositionOrder(IDString oid)
        {
            FXClientResult res = new FXClientResult();

            PositionFetchResult fetch_bpr = fetchBrokerPositionRecord(oid.ID);

            if (fetch_bpr.Error)
            {
                res.setError(fetch_bpr.Message, FXClientResponseType.Rejected, false);
                return res;
            }

            BrokerPositionRecord bpr = fetch_bpr.ResultObject;

            int orders_to_send;
            FXClientTaskObjectResult<AccountResult> ares;
            BrokerOrder bro = null;
            Account acct;
            BrokerOrderState orig_state;
            try
            {
                switch (oid.Type)
                {//handle update to the stop/target order here...there were no trades/orders to adjust
                    case IDType.Stop:
                        #region update position stop
                        if (bpr.StopOrder == null)
                        {//canceling an order which is already gone...allow it, but indicate the missing order
                            _log.captureDebug("stop order not found, but cancel allowed (assuming a hit is incoming)");
                            res.OrderMissing = true;
                            return res;
                        }

                        bro = bpr.StopOrder.BrokerOrder;
                        bro.StopPrice = 0.0;
                        orig_state = bro.OrderState;

                        if (bro.OrderState != BrokerOrderState.PendingCancel)
                        {
                            bro.OrderState = BrokerOrderState.PendingCancel;
                            _parent.FireOrderUpdated(bro, null, "canceling pstop");
                        }

                        ares = AccountResolution(TradeEntityID.CreateID(_opts.TradeEntityName, bro));
                        if (ares.Error)
                        {
                            res.setError(ares.Message, ares.FXClientResponse, ares.Disconnected);
                            return res;
                        }

                        ////////////////////
                        orders_to_send = 0;//FIX ME
                        acct = ares.ResultObject.FromOutChannel;
                        res = SubmitStopOrders(bpr, bro, acct);
                        if (res.Error)
                        {
                            if (res.OrderMissing)
                            {
                                _log.captureDebug("stop order missing, but cancel allowed (assuming a hit is incoming)");
                                res = new FXClientResult();
                                res.OrderMissing = true;
                                return res;
                            }
                            else if (res.OrdersSent == 0)
                            {
                                bro.OrderState = orig_state;
                                _parent.FireOrderUpdated(bro, null, "failing pstop cancel");
                                if (ares.TaskCompleted) { _parent.ResponseProcessor.DeactivateAccountResponder(acct.AccountId); }
                                return res;
                            }
                            else if (res.OrdersSent < orders_to_send)
                            {//FIX ME - what to do in this case....some orders were sent, but not all...
                                return res;
                            }

                            return res;
                        }

                        if (res.OrdersSent == 0)
                        {
                            bro.OrderState = BrokerOrderState.Cancelled;
                            bpr.StopOrder = null;
                            _parent.FireOrderUpdated(bro, null, "pstop cancelled");
                            if (ares.TaskCompleted)
                            { _parent.ResponseProcessor.DeactivateAccountResponder(acct.AccountId); }

                            FunctionResult fr = ClearAllFinalizedPositions();
                            if (fr.Error)
                            {
                                res.setError(fr.Message, FXClientResponseType.Rejected, false);
                                return res;
                            }
                        }
                        return res;
                    ////////////////////
                        #endregion
                    case IDType.Target:
                        #region update position target
                        if (bpr.TargetOrder == null)
                        {//canceling an order which is already gone...allow it, but indicate the missing order
                            _log.captureDebug("target order not found, but cancel allowed (assuming a hit is incoming)");
                            res.OrderMissing = true;
                            return res;
                        }

                        bro = bpr.TargetOrder.BrokerOrder;
                        bro.LimitPrice = 0.0;
                        orig_state = bro.OrderState;

                        if (bro.OrderState != BrokerOrderState.PendingCancel)
                        {
                            bro.OrderState = BrokerOrderState.PendingCancel;
                            _parent.FireOrderUpdated(bro, null, "canceling ptarget");
                        }

                        ares = AccountResolution(TradeEntityID.CreateID(_opts.TradeEntityName, bro));
                        if (ares.Error)
                        {
                            res.setError(ares.Message, ares.FXClientResponse, ares.Disconnected);
                            return res;
                        }


                        ////////////////////
                        orders_to_send = 0;//FIX ME
                        acct = ares.ResultObject.FromOutChannel;
                        res = SubmitTargetOrders(bpr, bro, acct);
                        if (res.Error)
                        {
                            if (res.OrderMissing)
                            {
                                _log.captureDebug("target order missing, but cancel allowed (assuming a hit is incoming)");
                                res = new FXClientResult();
                                res.OrderMissing = true;
                                return res;
                            }
                            else if (res.OrdersSent == 0)
                            {
                                bro.OrderState = orig_state;
                                _parent.FireOrderUpdated(bro, null, "failing ptarget cancel");
                                if (ares.TaskCompleted) { _parent.ResponseProcessor.DeactivateAccountResponder(acct.AccountId); }
                                return res;
                            }
                            else if (res.OrdersSent < orders_to_send)
                            {//FIX ME - what to do in this case....some orders were sent, but not all...
                                return res;
                            }

                            return res;
                        }
                        if (res.OrdersSent == 0)
                        {
                            bro.OrderState = BrokerOrderState.Cancelled;
                            bpr.TargetOrder = null;
                            _parent.FireOrderUpdated(bro, null, "cancel ptarget");
                            if (ares.TaskCompleted) { _parent.ResponseProcessor.DeactivateAccountResponder(acct.AccountId); }

                            FunctionResult fr = ClearAllFinalizedPositions();
                            if (fr.Error)
                            {
                                res.setError(fr.Message, FXClientResponseType.Rejected, false);
                                return res;
                            }
                        }
                        return res;
                    ////////////////////
                        #endregion
                    default:
                        res.setError("Unable to process order id prefix order ID '" + oid.ID + "'.", FXClientResponseType.Rejected, false);
                        return res;
                }
            }
            catch (Exception e)
            {
                _log.captureException(e);
                res.setError("UNEXPECTED EXCEPTION - " + e.Message, FXClientResponseType.Invalid, false);
                return res;
            }
        }

        public FXClientResult CancelSpecificOrder(IDString oid)
        {
            FXClientResult res = new FXClientResult();

            PositionFetchResult fetch_bpr = fetchBrokerPositionRecordByTradeID(oid.ID);
            if (fetch_bpr.Error)
            {
                res.setError(fetch_bpr.Message, FXClientResponseType.Rejected, false);
                return res;
            }
            BrokerPositionRecord bpr = fetch_bpr.ResultObject;

            if (!bpr.TradeRecords.ContainsKey(oid))
            {
                res.setError("Unable to locate trade record for specific order cancel. : oid='" + oid.ID + "'", FXClientResponseType.Rejected, false);
                res.OrderMissing = true;
                return res;
            }

            TradeRecord tr = bpr.TradeRecords[oid];
            BrokerOrder bro = tr.openOrder.BrokerOrder;

            #region verify specified order is an unfilled limit order
            if (bro.OrderType == OrderType.Market)
            {//market orders can not be canceled....
                //generally this is the result of a "post-hit" secondary order fill
                res.setError("Attempt to cancel a market order", FXClientResponseType.Rejected, false);
                tr.openOrder.CancelToClose = true;//expect RE to send a close on the order later (possibly under a different pos id)
                return res;//fail the cancel so RE knows the order is still open (which it is...)
            }
            if (bro.OrderType != OrderType.Limit)
            {
                res.setError("Canceling an unknown order type '" + bro.OrderType + "'.", FXClientResponseType.Rejected, false);
                return res;
            }

            if (bro.OrderState != BrokerOrderState.Submitted)
            {
                res.setError("Canceling an order in an unknown state '" + bro.OrderState + "'.", FXClientResponseType.Rejected, false);
                return res;
            }
            #endregion

            #region oanda close order
            FXClientTaskObjectResult<AccountResult> ares;
            FXClientObjectResult<LimitOrder> lores;

            ares = AccountResolution(TradeEntityID.CreateID(_opts.TradeEntityName, bro));
            if (ares.Error)
            {
                res.setError(ares.Message, ares.FXClientResponse, ares.Disconnected);
                return res;
            }

            lores = _parent.fxClient.GetOrderWithID(ares.ResultObject.FromOutChannel, oid.Num);
            if (lores.Error)
            {
                res.setError("Unable to locate the corresponding oanda broker order for id '" + oid.ID + "'.", lores.FXClientResponse, lores.Disconnected);
                if (ares.TaskCompleted) { _parent.ResponseProcessor.DeactivateAccountResponder(ares.ResultObject.FromOutChannel.AccountId); }
                return res;
            }
            return _parent.fxClient.SendOAClose(ares.ResultObject.FromOutChannel, lores.ResultObject);
            #endregion
        }

        public FXClientResult CancelAllOrders()
        {
            FXClientResult res = new FXClientResult();

            foreach (int act_id in _accounts.Accounts.Keys)
            {
                BrokerSymbolRecords bsr = _accounts.Accounts[act_id];
                FXClientObjectResult<AccountResult> ares = _parent.fxClient.ConvertStringToAccount(act_id.ToString());
                if (ares.Error)
                {
                    res.setError(ares.Message, ares.FXClientResponse, ares.Disconnected);
                    return res;
                }

                Account acct = ares.ResultObject.FromOutChannel;
                foreach (string pos_key in bsr.Keys)
                {
                    foreach (string bpr_key in bsr[pos_key].Positions.Keys)
                    {
                        BrokerPositionRecord bpr = bsr[pos_key].Positions[bpr_key];
                        foreach (IDString tr_key in bpr.TradeRecords.Keys)
                        {
                            TradeRecord tr = bpr.TradeRecords[tr_key];
                            BrokerOrder bro = tr.openOrder.BrokerOrder;

                            #region verify order type and state
                            if (bro.OrderType != OrderType.Limit)
                            {//FIX ME <-- is this right? should CancelAllOrders() close ALL pending AND open??
                                _log.captureDebug("  skipping open order type '" + bro.OrderState + "' id '" + bro.OrderId + "'.");
                                continue;
                            }

                            if (bro.OrderState != BrokerOrderState.Submitted)
                            {//FIX ME <-- is this right? should CancelAllOrders() close ALL pending AND open??
                                _log.captureDebug("  skipping open order state '" + bro.OrderState + "' id '" + bro.OrderId + "'.");
                                continue;
                            }
                            #endregion

                            #region oanda close limit order
                            FXClientObjectResult<LimitOrder> fres = _parent.fxClient.GetOrderWithID(acct, tr.OrderID.Num);
                            if (fres.Error)
                            {
                                if (fres.OrderMissing)
                                {
                                    _log.captureDebug("  skipping order '" + tr.OrderID.ID + "', it's gone missing (probably filled).");
                                    continue;
                                }

                                res.setError(fres.Message, fres.FXClientResponse, fres.Disconnected);
                                return res;
                            }
                            FXClientResult subres = _parent.fxClient.SendOAClose(acct, fres.ResultObject);
                            if (subres.Error)
                            {
                                if (fres.OrderMissing)
                                {
                                    _log.captureDebug("  skipping order '" + tr.OrderID.ID + "', it's gone missing (probably filled).");
                                    continue;
                                }

                                return subres;
                            }
                            #endregion
                        }
                    }
                }
            }

            res.FXClientResponse = FXClientResponseType.Accepted;
            return res;
        }
        #endregion

        #region account transaction handler
        public FXClientTaskResult HandleAccountTransaction(ResponseRecord response)
        {
            FXClientTaskResult result = new FXClientTaskResult();

            result.TaskCompleted = true;
            try
            {
                Transaction trans = response.Transaction;
                string desc = trans.Description;
                int link_id = trans.Link;

                _log.captureDebug("handleAccountTransaction() called.");
                _log.writeTransaction("  HANDLING TRANSACTION", trans, response.AccountId);

                if (!_parent.fxClient.IsInit)
                {
                    result.setError("Disconnected : Broker not connected!");
                    result.TaskCompleted = false;
                    result.FXClientResponse = FXClientResponseType.Disconnected;
                    return result;
                }
                TransactionFetchResult fetch_bpr = fetchBrokerPositionRecordByTransResponse(response);
                if (fetch_bpr.Error)
                {
                    //a limit order has been filled, once filled oanda gives it a new id
                    if (desc == "Buy Order Filled" || desc == "Sell Order Filled")
                    {//add the new id and the fill to the queue of fill records
                        FXClientObjectResult<Fill> fillres = _parent.fxClient.GenerateFillFromTransaction(trans, response.BaseCurrency);
                        if (fillres.Error)
                        {
                            result.setError(fillres.Message, fillres.FXClientResponse, fillres.Disconnected);
                            return result;
                        }
                        _fill_queue.AddNewFillRecord(trans.Base + "/" + trans.Quote, fillres.ResultObject, trans.TransactionNumber, trans.Link);
                        return result;//wait for the "Order Fulfilled" event to finalize the original limit order
                    }

                    _log.captureDebug("  TRANSACTION ISSUE : '" + fetch_bpr.Message + "'");
                    _parent.RESendNoMatch(response);
                    _accounts.LogData(_log);
                    return result;
                }

                BrokerPositionRecord pos = fetch_bpr.ResultObject;
                TradeRecord tr = fetch_bpr.TransactionTradeRecord;

                if (!fetch_bpr.IsLinked)
                {//transaction matched an open order directly...
                    #region open order direct id match
                    if (desc == "Buy Order" || //long limit order response
                        desc == "Sell Order")  //short limit order response
                    {//the fill on a limit comes in under a linked id, 
                        return result;//so do nothing here
                    }
                    else if (desc == "Buy Market" || //long market order response
                             desc == "Sell Market")  //short market order response
                    {
                        FXClientObjectResult<Fill> fillres = _parent.fxClient.GenerateFillFromTransaction(trans, response.BaseCurrency);
                        if (fillres.Error)
                        {
                            result.setError(fillres.Message, fillres.FXClientResponse, fillres.Disconnected);
                            return result;
                        }

                        _parent.RESendFilledOrder(fillres.ResultObject, tr.openOrder.BrokerOrder, "market order open");
                        return result;
                    }
                    else
                    {
                        result.setError("Uknown direct match transaction (num='" + trans.TransactionNumber + "') description. : '" + desc + "'");
                        return result;
                    }
                    #endregion
                }
                else
                {//transaction is linked to the open order
                    string oclabel = "Order Cancel";
                    int oclabel_len = oclabel.Length;
                    #region open order linked id match
                    if (desc == "Order Fulfilled")
                    {//this transaction is a notice that a limit order has been filled
                        #region limit order filled

                        if (tr.openOrder.BrokerOrder.OrderType == OrderType.Limit)
                        {
                            FunctionObjectResult<FillRecord> fillres;

                            switch (tr.openOrder.BrokerOrder.OrderState)
                            {
                                case BrokerOrderState.Submitted:
                                    _log.captureDebug("    looking for fill to submitted limit order id='" + tr.openOrder.BrokerOrder.OrderId + "' size ='" + tr.openOrder.BrokerOrder.Shares + "'");
                                    fillres = _fill_queue.FindOpenFill(fetch_bpr.SymbolName, tr.openOrder.BrokerOrder.OrderId, (int)tr.openOrder.BrokerOrder.Shares, _log);
                                    if (fillres.Error)
                                    {
                                        result.setError(fillres.Message);
                                        return result;
                                    }

                                    //if the fill record qty == limit order qty && queue count==1 then fill ok, qty ok
                                    if (tr.openOrder.BrokerOrder.Shares == fillres.ResultObject.Fill.Quantity)
                                    {
                                        if (_fill_queue.QueueCount(fetch_bpr.SymbolName) != 0)
                                        {//error
                                            result.setError("fill matched order quantity, but fills remain in the queue");
                                            return result;
                                        }
                                        if (fillres.ResultObject.LinkId == "0")
                                        {//standard full fill hit with no linked orders
                                            tr.openOrder.FillId = fillres.ResultObject.Id;
                                            _parent.RESendFilledOrder(fillres.ResultObject.Fill, tr.openOrder.BrokerOrder, "limit order fullfilment");
                                            return result;
                                        }
                                        else//LinkID != "0"
                                        {//target order fully filled by matching order
                                            //send target order straight to close

                                            //FIX ME - setup and fill the target's close order
                                            BrokerOrder tbo = new BrokerOrder();//<-- can't really do this...RE will baulk..
                                            tbo.OrderSymbol = fetch_bpr.ResultObject.Symbol;

                                            Fill tcfill = new Fill();
                                            tr.closeOrder = new OrderRecord(tbo, false);
                                            _parent.RESendFilledOrder(tcfill, tr.closeOrder.BrokerOrder, "limit order filled open order");

                                            //send the matched order to close with the target price
                                            PositionFetchResult fetch_mbpr = fetchBrokerPositionRecordByTradeID(fillres.ResultObject.LinkId);
                                            BrokerPositionRecord mbpr = fetch_mbpr.ResultObject;
                                            TradeRecord mtr = mbpr.TradeRecords[new IDString(fillres.ResultObject.LinkId)];

                                            //FIX ME - setup and fill the matched close order
                                            BrokerOrder mbo = new BrokerOrder();//<-- can't really do this...RE will baulk..
                                            mbo.OrderSymbol = mbpr.Symbol;
                                            Fill mcfill = new Fill();
                                            mtr.closeOrder = new OrderRecord(mbo, false);
                                            _parent.RESendFilledOrder(mcfill, mtr.closeOrder.BrokerOrder, "open order closed on limit fill");

                                            return result;
                                        }
                                    }
                                    //if the fill record qty < limit order qty && queue count > 1 then fill ok, qty change
                                    else if (tr.openOrder.BrokerOrder.Shares > fillres.ResultObject.Fill.Quantity)
                                    {
                                        FillRecord linkless_fill = fillres.ResultObject;
                                        //be sure there are more fill records queued up...
                                        if (_fill_queue.QueueCount(fetch_bpr.SymbolName) < 1)
                                        {
                                            result.setError("fill smaller than order, but no fills remain in the queue");
                                            return result;
                                        }

                                        //check the qty's to be sure the fills add up to a "full fill"
                                        if (tr.openOrder.BrokerOrder.Shares != _fill_queue.QueueUnitTotal(fetch_bpr.SymbolName))
                                        {
                                            result.setError("fills don't add up to ordered shares.");
                                            return result;
                                        }

                                        //change main order qty
                                        tr.openOrder.BrokerOrder.Shares = linkless_fill.Fill.Quantity;

                                        tr.openOrder.FillId = fillres.ResultObject.Id;
                                        _parent.RESendFilledOrder(fillres.ResultObject.Fill, tr.openOrder.BrokerOrder, "limit order fullfilment");

                                        do
                                        {
                                            fillres = _fill_queue.GetNextFill(fetch_bpr.SymbolName);
                                            if (fillres.Error)
                                            {
                                                result.setError(fillres.Message);
                                                return result;
                                            }

                                            //look for the order with an id or linkid of fill.LinkId
                                            PositionFetchResult lp = fetchBrokerPositionRecordByTradeID(fillres.ResultObject.LinkId);
                                            if (lp.Error)
                                            {
                                                result.setError(lp.Message);
                                                return result;
                                            }

                                            //FIX ME <-----
                                            //process this fill on the found order
                                            //FIX ME <-----

                                        } while (_fill_queue.QueueCount(fetch_bpr.SymbolName) > 0);
                                        return result;
                                    }
                                    else// shares < fill.qty
                                    {//should this be the same as '>' above??
                                        result.setError("Shares smaller than fill qty.");
                                        return result;
                                    }
                                default:
                                    result.setError("TBI - this should be more thorough...not all states are unusable, and those that are need an Update()");
                                    return result;
                            }
                        }
                        else
                        {
                            result.setError("Order fullfilment event received on non-limit order id '" + tr.openOrder.BrokerOrder.OrderId + "' type '" + tr.openOrder.BrokerOrder.OrderType + "'.");
                            return result;
                        }

                        #endregion
                    }
                    else if (desc == "Cancel Order")
                    {
                        #region cancel order
                        tr.openOrder.BrokerOrder.OrderState = BrokerOrderState.Cancelled;
                        _parent.FireOrderUpdated(tr.openOrder.BrokerOrder, null, "order canceled");

                        FXClientResult fres = finalizePositionExit(pos, "cancel order");
                        if (fres.Error)
                        {
                            result.setError(fres.Message, fres.FXClientResponse, fres.Disconnected);
                        }
                        return result;
                        #endregion
                    }
                    else if ( desc.Length > oclabel_len && desc.Substring(0, oclabel_len) == oclabel )
                    {
                        #region order canceled by the server
                        int si, ei;
                        si = desc.IndexOf('(')+1;
                        ei = desc.LastIndexOf(')');
                        string reason;
                        if(si>=0 && si < desc.Length && ei>=0 && ei-si< desc.Length)
                        {reason= "{" + desc.Substring(si, ei-si) + "}";}
                        else
                        {reason = "{}";}

                        _log.captureDebug("order canceled reason : '" + reason + "'");
                        
                        tr.openOrder.BrokerOrder.OrderState = BrokerOrderState.Cancelled;
                        _parent.FireOrderUpdated(tr.openOrder.BrokerOrder, null, "order canceled" + reason);

                        FXClientResult fres = finalizePositionExit(pos, "order canceled" + reason);
                        if (fres.Error)
                        {
                            result.setError(fres.Message, fres.FXClientResponse, fres.Disconnected);
                        }
                        return result;
                        #endregion
                    }
                    else if (desc == "Close Trade")
                    {
                        #region close trade
                        if (tr.closeOrder == null)
                        {//FIX ME -- this can't work....the plugin has now way to tell RE what's happening because BrokerOrders can't be initiated here
                            BrokerOrder nbo = new BrokerOrder();

                            //setup nbo as close submitted...
                            nbo.OrderState = BrokerOrderState.Submitted;
                            nbo.OrderSymbol = tr.openOrder.BrokerOrder.OrderSymbol;
                            nbo.OrderType = OrderType.Market;
                            if (tr.openOrder.BrokerOrder.TransactionType == TransactionType.Buy)
                            { nbo.TransactionType = TransactionType.Sell; }
                            if (tr.openOrder.BrokerOrder.TransactionType == TransactionType.Short)
                            { nbo.TransactionType = TransactionType.Cover; }
                            nbo.PositionID = tr.openOrder.BrokerOrder.PositionID;
                            nbo.Shares = trans.Units;
                            nbo.SubmittedDate = DateTime.Now;
                            IDString ids = new IDString(IDType.Close, int.Parse(tr.openOrder.BrokerOrder.OrderId));
                            nbo.OrderId = ids.ID;

                            //send re close submitted before sending the fill
                            _parent.FireOrderUpdated(nbo, null, "external close trade");//FIX ME <-- this will cause trouble with RE
                        }

                        FXClientObjectResult<Fill> fillres = _parent.fxClient.GenerateFillFromTransaction(trans, response.BaseCurrency);
                        if (fillres.Error)
                        {
                            result.FXClientResponse = fillres.FXClientResponse;
                            result.setError(fillres.Message);
                            return result;
                        }

                        if (pos.CloseOrder == null)
                        { _parent.RESendFilledOrder(fillres.ResultObject, tr.closeOrder.BrokerOrder, "close trade"); }
                        else
                        {
                            BrokerOrderState fill_state = _parent.RESendFilledOrder(fillres.ResultObject, pos.CloseOrder.BrokerOrder, "close position trade");
                            if (fill_state == BrokerOrderState.Filled)
                            { pos.CloseOrder = null; }
                        }
                        tr.closeOrder = null;

                        if (pos.StopOrder == null && pos.TargetOrder == null && tr.closeOrder == null) { tr.openOrder = null; }
                        return result;
                        #endregion
                    }
                    else if (desc == "Close Position")
                    {
                        #region close position
                        if (tr.closeOrder == null)
                        {
                            BrokerOrder nbo = new BrokerOrder();

                            //setup nbo as close submitted...
                            nbo.OrderState = BrokerOrderState.Submitted;
                            nbo.OrderSymbol = tr.openOrder.BrokerOrder.OrderSymbol;
                            nbo.OrderType = OrderType.Market;
                            if (tr.openOrder.BrokerOrder.TransactionType == TransactionType.Buy)
                            { nbo.TransactionType = TransactionType.Sell; }
                            if (tr.openOrder.BrokerOrder.TransactionType == TransactionType.Short)
                            { nbo.TransactionType = TransactionType.Cover; }
                            nbo.PositionID = tr.openOrder.BrokerOrder.PositionID;
                            nbo.Shares = trans.Units;
                            nbo.SubmittedDate = DateTime.Now;
                            IDString ids = new IDString(IDType.Close, int.Parse(tr.openOrder.BrokerOrder.OrderId));
                            nbo.OrderId = ids.ID;

                            //send re close submitted before sending the fill
                            _parent.FireOrderUpdated(nbo, null, "external close position");
                        }

                        FXClientObjectResult<Fill> fillres = _parent.fxClient.GenerateFillFromTransaction(trans, response.BaseCurrency);
                        if (fillres.Error)
                        {
                            result.FXClientResponse = fillres.FXClientResponse;
                            result.setError(fillres.Message);
                            return result;
                        }

                        if (pos.CloseOrder == null)
                        { _parent.RESendFilledOrder(fillres.ResultObject, tr.closeOrder.BrokerOrder, "close position"); }
                        else
                        {
                            BrokerOrderState fill_state = _parent.RESendFilledOrder(fillres.ResultObject, pos.CloseOrder.BrokerOrder, "close position position");
                            if (fill_state == BrokerOrderState.Filled)
                            { pos.CloseOrder = null; }
                        }

                        tr.closeOrder = null;

                        if (pos.StopOrder == null && pos.TargetOrder == null && tr.closeOrder == null) { tr.openOrder = null; }
                        return result;
                        #endregion
                    }
                    else if (desc == "Modify Trade")
                    {//modify response...
                        #region modify trade
                        double sl = trans.Stop_loss;
                        double tp = trans.Take_profit;

                        _log.captureDebug("  handleAccountTransaction() - preparing to modify a trade (trans num='" + trans.TransactionNumber + "'/link='" + trans.Link + "' tp='" + tp + "'/sl='" + sl + "')...");

                        if (tr.openOrder == null)
                        {
                            result.setError("Missing TradeRecord openOrder object.");
                            return result;
                        }

                        if (sl != tr.openOrder.StopPrice)
                        {//stop changed
                            if (pos.StopOrder == null)
                            {
                                result.setError("Missing Position StopOrder object.");
                                return result;
                            }

                            tr.openOrder.StopPrice = sl;
                            if (sl == 0.0 && pos.StopOrder.BrokerOrder.OrderState == BrokerOrderState.PendingCancel)
                            {//then count 'cancel fills' and update stopOrder when done
                                pos.StopOrder.FillQty += (int)tr.openOrder.BrokerOrder.Shares;
                                if (pos.StopOrder.FillQty >= pos.StopOrder.BrokerOrder.Shares)
                                {
                                    BrokerOrder sbo = pos.StopOrder.BrokerOrder;
                                    pos.StopOrder = null;
                                    sbo.OrderState = BrokerOrderState.Cancelled;
                                    _parent.FireOrderUpdated(sbo, null, "Cancel stop");
                                }
                            }
                            return result;
                        }
                        else if (tp != tr.openOrder.TargetPrice)
                        {//target changed
                            if (pos.TargetOrder == null)
                            {
                                result.setError("Missing Position TargetOrder object.");
                                return result;
                            }
                            tr.openOrder.TargetPrice = tp;
                            if (tp == 0.0 && pos.TargetOrder.BrokerOrder.OrderState == BrokerOrderState.PendingCancel)
                            {//then count 'cancel fills' and update stopOrder when done
                                pos.TargetOrder.FillQty += (int)tr.openOrder.BrokerOrder.Shares;
                                if (pos.TargetOrder.FillQty >= pos.TargetOrder.BrokerOrder.Shares)
                                {
                                    BrokerOrder tbo = pos.TargetOrder.BrokerOrder;
                                    pos.TargetOrder = null;
                                    tbo.OrderState = BrokerOrderState.Cancelled;
                                    _parent.FireOrderUpdated(tbo, null, "Cancel target");
                                }
                            }
                            return result;
                        }
                        else
                        {
                            result.setError("Oanda 'Modify Trade' event type changed something (other than the stop loss/profit limit prices) on order '" + tr.openOrder.BrokerOrder.OrderId + "'.");
                            return result;
                        }
                        #endregion
                    }
                    else if (desc == "Stop Loss")
                    {
                        #region stop loss
                        if (pos.StopOrder == null)
                        {//sometimes oanda accepts a cancel order THEN sends a fill...the broker order has already been canceled and there is no way to open a new one from the plugin
                            result.setError("*** CATASTROPHIC FAILURE *** missing position stop order in stop loss handler!");
                            return result;
                        }

                        FXClientObjectResult<Fill> fillres = _parent.fxClient.GenerateFillFromTransaction(trans, response.BaseCurrency);
                        if (fillres.Error)
                        {
                            result.setError(fillres.Message, fillres.FXClientResponse, fillres.Disconnected);
                            return result;
                        }

                        tr.openOrder.StopHit = true;
                        _parent.RESendFilledOrder(fillres.ResultObject, pos.StopOrder.BrokerOrder, "stop loss");

                        if (tr.closeOrder != null)
                        {
                            BrokerOrder cbo = tr.closeOrder.BrokerOrder;
                            bool isre = tr.closeOrder.IsRightEdgeOrder;
                            tr.closeOrder = null;

                            if (isre)
                            {
                                cbo.OrderState = BrokerOrderState.Cancelled;
                                _parent.FireOrderUpdated(cbo, null, "close order canceled on stop hit");
                            }
                        }

                        pos.StopOrder.FillQty += fillres.ResultObject.Quantity;

                        _log.captureDebug("stop fill [order fill='" + fillres.ResultObject.Quantity + "' total filled='" + pos.StopOrder.FillQty + "'] stop shares='" + pos.StopOrder.BrokerOrder.Shares + "'");
                        if (pos.StopOrder.FillQty >= pos.StopOrder.BrokerOrder.Shares)
                        {
                            FXClientResult res = finalizePositionExit(pos, "stop hit");
                            if (res.Error) { result.setError(res.Message, res.FXClientResponse, res.Disconnected); }
                            return result;
                        }
                        return result;
                        #endregion
                    }
                    else if (desc == "Take Profit")
                    {
                        #region take profit
                        if (pos.TargetOrder == null)
                        {//sometimes oanda accepts a cancel order THEN sends a fill...the broker order has already been canceled and there is no way to open a new one from the plugin
                            result.setError("*** CATASTROPHIC FAILURE *** missing position target order in take profit handler!");
                            return result;
                        }

                        FXClientObjectResult<Fill> fillres = _parent.fxClient.GenerateFillFromTransaction(trans, response.BaseCurrency);
                        if (fillres.Error)
                        {
                            result.setError(fillres.Message, fillres.FXClientResponse, fillres.Disconnected);
                            return result;
                        }

                        tr.openOrder.TargetHit = true;
                        _parent.RESendFilledOrder(fillres.ResultObject, pos.TargetOrder.BrokerOrder, "target");

                        if (tr.closeOrder != null)
                        {
                            BrokerOrder cbo = tr.closeOrder.BrokerOrder;
                            bool isre = tr.closeOrder.IsRightEdgeOrder;
                            tr.closeOrder = null;
                            if (isre)
                            {
                                cbo.OrderState = BrokerOrderState.Cancelled;
                                _parent.FireOrderUpdated(cbo, null, "close order canceled on target hit");
                            }
                        }

                        pos.TargetOrder.FillQty += fillres.ResultObject.Quantity;

                        _log.captureDebug("target fill [order fill='" + fillres.ResultObject.Quantity + "' total filled='" + pos.TargetOrder.FillQty + "'] target shares='" + pos.TargetOrder.BrokerOrder.Shares + "'");
                        if (pos.TargetOrder.FillQty >= pos.TargetOrder.BrokerOrder.Shares)
                        {
                            FXClientResult res = finalizePositionExit(pos, "target hit");
                            if (res.Error) { result.setError(res.Message, res.FXClientResponse, res.Disconnected); }
                            return result;
                        }
                        return result;
                        #endregion
                    }
                    else if (desc == "Order Expired")
                    {
                        #region order expired
                        tr.openOrder.BrokerOrder.OrderState = BrokerOrderState.Cancelled;
                        _parent.FireOrderUpdated(tr.openOrder.BrokerOrder, null, "order expired");

                        FXClientResult fres = finalizePositionExit(pos, "order expired");
                        if (fres.Error)
                        {
                            result.setError(fres.Message, fres.FXClientResponse, fres.Disconnected);
                        }
                        return result;
                        #endregion
                    }
                    else if (desc == "Buy Order Filled" || desc == "Sell Order Filled")
                    {
                        _log.captureDebug("    limit fill matched market order (num='" + trans.TransactionNumber + "',link='" + trans.Link + "'). : '" + desc + "'");
                        FXClientObjectResult<Fill> fillres = _parent.fxClient.GenerateFillFromTransaction(trans, response.BaseCurrency);
                        if (fillres.Error)
                        {
                            result.setError(fillres.Message, fillres.FXClientResponse, fillres.Disconnected);
                            return result;
                        }

                        _fill_queue.AddNewFillRecord(trans.Base + "/" + trans.Quote, fillres.ResultObject, trans.TransactionNumber, trans.Link);
                        return result;
                    }
                    else
                    {
                        result.setError("Unknown link matched transaction description (num='" + trans.TransactionNumber + "',link='" + trans.Link + "'). : '" + desc + "'");
                        return result;
                    }
                    #endregion
                }
            }
            catch (Exception e)
            {
                _log.captureException(e);
                result.setError("Unhandled exception : " + e.Message);
                return result;
            }
        }
        #endregion

        #region orderbook state inquiry functions
        public List<BrokerOrder> GetOpenBrokerOrderList()
        {
            List<BrokerOrder> list = new List<BrokerOrder>();
            foreach (int act_id in _accounts.Accounts.Keys)
            {
                BrokerSymbolRecords bsr = _accounts.Accounts[act_id];

                foreach (string sym_key in bsr.Keys)
                {
                    BrokerPositionRecords bprl = bsr[sym_key];
                    foreach (string bpr_key in bprl.Positions.Keys)
                    {
                        BrokerPositionRecord bpr = bprl.Positions[bpr_key];

                        //FIX ME - what order states/types does RE really want here...

                        if (bpr.StopOrder != null) { list.Add(bpr.StopOrder.BrokerOrder.Clone()); }
                        if (bpr.TargetOrder != null) { list.Add(bpr.TargetOrder.BrokerOrder.Clone()); }
                        foreach (IDString tr_key in bpr.TradeRecords.Keys)
                        {
                            TradeRecord tr = bpr.TradeRecords[tr_key];
                            list.Add(tr.openOrder.BrokerOrder.Clone());
                        }
                    }//end of Positions loop
                }//end of symbol loop
            }//end of accounts loop
            return list;
        }
        
        public BrokerOrder GetOpenBrokerOrder(string id)
        {
            //it may be better to analyze the id and call the position/trade fetch routines instead...
            //but for now this works fine...

            foreach (int act_id in _accounts.Accounts.Keys)
            {
                BrokerSymbolRecords bsr = _accounts.Accounts[act_id];

                foreach (string sym_key in bsr.Keys)
                {
                    BrokerPositionRecords bprl = bsr[sym_key];
                    foreach (string bpr_key in bprl.Positions.Keys)
                    {
                        BrokerPositionRecord bpr = bprl.Positions[bpr_key];
                        if (bpr.StopOrder != null && bpr.StopOrder.BrokerOrder != null && bpr.StopOrder.BrokerOrder.OrderId == id)
                        { return (bpr.StopOrder.BrokerOrder); }
                        else if (bpr.TargetOrder != null && bpr.TargetOrder.BrokerOrder != null && bpr.TargetOrder.BrokerOrder.OrderId == id)
                        { return (bpr.TargetOrder.BrokerOrder); }

                        foreach (IDString tr_key in bpr.TradeRecords.Keys)
                        {
                            TradeRecord tr = bpr.TradeRecords[tr_key];
                            if (tr.openOrder.BrokerOrder!=null && tr.openOrder.BrokerOrder.OrderId == id)
                            { return (tr.openOrder.BrokerOrder); }
                        }//end of trade record loop
                    }//end of position loop
                }//end of symbol loop
            }//end of account loop
            return null;
        }

        public FunctionObjectResult<int> GetSymbolShares(string symbol_name)
        {
            FunctionObjectResult<int> res = new FunctionObjectResult<int>();
            res.ResultObject = 0;
            int n = 0;

            try
            {
                string l_act = _trade_entities.GetAccount(TradeEntityID.CreateID(_opts.TradeEntityName, symbol_name, PositionType.Long));
                string s_act = _trade_entities.GetAccount(TradeEntityID.CreateID(_opts.TradeEntityName, symbol_name, PositionType.Short));

                int act_id = int.Parse(l_act);
                if (_accounts.Accounts.ContainsKey(act_id))
                {
                    BrokerSymbolRecords bsr = _accounts.Accounts[act_id];
                    if (bsr.ContainsKey(symbol_name))
                    {
                        n = bsr[symbol_name].getTotalSize();
                    }
                }
                act_id = int.Parse(s_act);
                if (_accounts.Accounts.ContainsKey(act_id))
                {
                    BrokerSymbolRecords bsr = _accounts.Accounts[act_id];
                    if (bsr.ContainsKey(symbol_name))
                    {
                        n -= bsr[symbol_name].getTotalSize();
                    }
                }
            }
            catch (Exception e)
            {
                res.setError(e.Message);
                return res;
            }

            res.ResultObject = n;
            return res;
        }

        public FXClientObjectResult<double> GetMarginAvailable()
        {
            if (_opts.AccountValuesEnabled)
            {
                FXClientObjectResult<double> res = new FXClientObjectResult<double>();
                res.ResultObject = 0.0;

                FXClientObjectResult<List<AccountResult>> getres = _parent.fxClient.GetFullAccountsList();
                if (getres.Error)
                {
                    res.setError(getres.Message, getres.FXClientResponse, getres.Disconnected);
                    return res;
                }
                List<AccountResult> act_list = getres.ResultObject;

                foreach (AccountResult ar in act_list)
                {
                    Account a = ar.FromOutChannel;
                    double ma, mr, mu;
                    ma = mr = mu = 0.0;

                    FXClientObjectResult<double> mres = _parent.fxClient.GetMarginAvailable(a);
                    if (mres.Error)
                    {
                        res.setError(mres.Message, mres.FXClientResponse, mres.Disconnected);
                        return res;
                    }
                    ma = mres.ResultObject;
                    res.ResultObject += ma;

                    mres = _parent.fxClient.GetMarginRate(a);
                    if (mres.Error)
                    {
                        res.setError(mres.Message, mres.FXClientResponse, mres.Disconnected);
                        return res;
                    }
                    mr = mres.ResultObject;

                    mres = _parent.fxClient.GetMarginUsed(a);
                    if (mres.Error)
                    {
                        res.setError(mres.Message, mres.FXClientResponse, mres.Disconnected);
                        return res;
                    }
                    mu = mres.ResultObject;

                    _account_values.SetMargin(a.AccountId.ToString(), a.HomeCurrency, ma, mu, mr);
                }

                FunctionResult fres = _account_values.saveSettings<AccountValuesStore>();
                if (fres.Error) { res.setError(fres.Message, FXClientResponseType.Invalid, false); }
                return res;
            }
            else
            {//FIX ME - this should still follow the trade entities default account mechanism...not just go straight to the opts..
                FXClientObjectResult<AccountResult> ares = _parent.fxClient.ConvertStringToAccount(_opts.DefaultAccount);
                if (ares.Error)
                {
                    FXClientObjectResult<double> res = new FXClientObjectResult<double>();
                    res.setError(ares.Message, ares.FXClientResponse, ares.Disconnected);
                    return res;
                }

                return _parent.fxClient.GetMarginAvailable(ares.ResultObject.FromOutChannel);
            }
        }

        public FXClientObjectResult<double> GetBuyingPower()
        {
            if (_opts.AccountValuesEnabled)
            {
                FXClientObjectResult<double> res = new FXClientObjectResult<double>();
                res.ResultObject = 0.0;

                FXClientObjectResult<List<AccountResult>> getres = _parent.fxClient.GetFullAccountsList();
                if (getres.Error)
                {
                    res.setError(getres.Message, getres.FXClientResponse, getres.Disconnected);
                    return res;
                }
                List<AccountResult> act_list = getres.ResultObject;

                foreach (AccountResult ar in act_list)
                {
                    Account a = ar.FromOutChannel;

                    FXClientObjectResult<double> bpres = _parent.fxClient.GetBalance(a);
                    if (bpres.Error)
                    {
                        res.setError(bpres.Message, bpres.FXClientResponse, bpres.Disconnected);
                        return res;
                    }

                    res.ResultObject += bpres.ResultObject;

                    _account_values.SetBalance(a.AccountId.ToString(), a.HomeCurrency, bpres.ResultObject);
                }

                FunctionResult fres = _account_values.saveSettings<AccountValuesStore>();
                if (fres.Error) { res.setError(fres.Message, FXClientResponseType.Invalid, false); }
                return res;
            }
            else
            {//FIX ME - this should still follow the trade entities default account mechanism...not just go straight to the opts..
                FXClientObjectResult<AccountResult> ares = _parent.fxClient.ConvertStringToAccount(_opts.DefaultAccount);
                if (ares.Error)
                {
                    FXClientObjectResult<double> res = new FXClientObjectResult<double>();
                    res.setError(ares.Message, ares.FXClientResponse, ares.Disconnected);
                    return res;
                }

                return _parent.fxClient.GetBalance(ares.ResultObject.FromOutChannel);
            }
        }
        #endregion

        #region position finalizers
        private FXClientResult finalizePositionExit(BrokerPositionRecord pos, string s)
        {
            _log.captureDebug("attempting to finalize position on " + s);

            if (pos.CloseOrder != null)
            {
                BrokerOrder cbo = pos.CloseOrder.BrokerOrder;

                if (cbo.OrderState == BrokerOrderState.PendingCancel)
                {
                    _log.captureDebug("finalizer canceling position close");
                    bool isre = pos.CloseOrder.IsRightEdgeOrder;
                    pos.CloseOrder = null;
                    if (isre)
                    {
                        cbo.OrderState = BrokerOrderState.Cancelled;
                        _parent.FireOrderUpdated(cbo, null, "close position canceled on " + s);
                    }
                }
                else if (cbo.OrderState == BrokerOrderState.Filled)
                {
                    pos.CloseOrder = null;
                }
            }

            if (pos.TargetOrder != null)
            {
                BrokerOrder tbo = pos.TargetOrder.BrokerOrder;

                if (pos.TargetOrder.BrokerOrder.OrderState == BrokerOrderState.PendingCancel)
                {
                    _log.captureDebug("finalizer canceling position target");
                    bool isre = pos.TargetOrder.IsRightEdgeOrder;
                    pos.TargetOrder = null;
                    if (isre)
                    {
                        tbo.OrderState = BrokerOrderState.Cancelled;
                        _parent.FireOrderUpdated(tbo, null, "ptarget canceled on " + s);
                    }
                }
                else if (pos.TargetOrder.BrokerOrder.OrderState == BrokerOrderState.Filled)
                {
                    pos.TargetOrder = null;
                }
            }

            if (pos.StopOrder != null)
            {
                BrokerOrder sbo = pos.StopOrder.BrokerOrder;

                if (sbo.OrderState == BrokerOrderState.PendingCancel)
                {
                    _log.captureDebug("finalizer canceling position stop");
                    bool isre = pos.StopOrder.IsRightEdgeOrder;
                    pos.StopOrder = null;
                    if (isre)
                    {
                        sbo.OrderState = BrokerOrderState.Cancelled;
                        _parent.FireOrderUpdated(sbo, null, "pstop canceled on " + s);
                    }
                }
                else if (sbo.OrderState == BrokerOrderState.Filled)
                {
                    pos.StopOrder = null;
                }
            }

            return new FXClientResult();
        }

        public FunctionResult ClearAllFinalizedPositions()
        {
            FunctionResult res = new FunctionResult();

            //_log.captureDebug("ClearAllFinalizedPositions() - Pre-Run Order Book Data");
            //_accounts.LogData(_log);

            List<int> act_keys = new List<int>(_accounts.Accounts.Keys);
            foreach (int act_id in act_keys)
            {
                if (!_accounts.Accounts.ContainsKey(act_id)) { continue; }
                BrokerSymbolRecords bsrl = _accounts.Accounts[act_id];
                List<string> sym_keys = new List<string>(bsrl.Keys);
                foreach (string sym_id in sym_keys)
                {
                    if (!bsrl.ContainsKey(sym_id)) { continue; }
                    BrokerPositionRecords bprl = bsrl[sym_id];
                    List<string> pos_keys = new List<string>(bprl.Positions.Keys);
                    foreach (string pos_id in pos_keys)
                    {
                        if (!bprl.Positions.ContainsKey(pos_id)) { continue; }
                        BrokerPositionRecord bpr = bprl.Positions[pos_id];

                        if (bpr.CloseOrder == null && bpr.TargetOrder == null && bpr.StopOrder == null)
                        {
                            bool safe_to_remove = true;
                            foreach (IDString tr_id in bpr.TradeRecords.Keys)
                            {
                                TradeRecord tr = bpr.TradeRecords[tr_id];
                                if (tr.closeOrder != null)
                                {
                                    safe_to_remove = false;
                                    break;
                                }
                                if (tr.openOrder != null)
                                {
                                    if (tr.openOrder.OrderType == OrderType.Limit &&
                                         tr.openOrder.BrokerOrderState == BrokerOrderState.Cancelled &&
                                        (tr.openOrder.TransactionType == TransactionType.Buy || tr.openOrder.TransactionType == TransactionType.Short)
                                        )
                                    {//if the open order is a fully cancelled limit buy/short go ahead and clear it
                                        continue;
                                    }
                                    else if (!tr.openOrder.StopHit && !tr.openOrder.TargetHit)
                                    {//the open order is not cancelled, nor is the stop/target hit
                                        safe_to_remove = false;//do not remove it...
                                        break;
                                    }
                                }
                            }
                            if (safe_to_remove)
                            {
                                _log.captureDebug("Finalizer Removing Position : pid='" + pos_id + "' sym='" + sym_id + "' act='" + act_id + "'");
                                if (!bprl.Positions.Remove(pos_id))
                                {
                                    res.setError("Unable to remove position record '" + pos_id + "'");
                                    return res;
                                }
                                if (bprl.Positions.Count == 0)
                                {
                                    _log.captureDebug("Finalizer Removing Symbol Page :  sym='" + sym_id + "' act='" + act_id + "'");
                                    if (!bsrl.Remove(sym_id))
                                    {
                                        res.setError("Unable to remove symbol record '" + sym_id + "'");
                                        return res;
                                    }
                                }
                                if (bsrl.Count == 0)
                                {
                                    _log.captureDebug("Finalizer Removing Account Page : act='" + act_id + "'");
                                    if (!_accounts.Accounts.Remove(act_id))
                                    {
                                        res.setError("Unable to remove account record '" + act_id + "'");
                                        return res;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            
            //_log.captureDebug("ClearAllFinalizedPositions() - Post-Run Order Book Data");
            //_accounts.LogData(_log);

            return res;
        }
        #endregion

        public FunctionResult saveSettings()
        {
            _accounts.FileName = _opts.OrderLogFileName;
            return _accounts.saveSettings<OrderBookData>();
        }

        public FunctionResult loadSettings(string opt_fname)
        {
            FunctionResult res = _accounts.loadSettings<OrderBookData>(opt_fname);
            if (res.Error) { return res; }
            return new FunctionResult();
        }

        private void SetOrderBookData(OrderBookData obd)
        {
            _accounts = obd;
        }

        public FunctionResult SetAccountState(BrokerAccountState accountState)
        {//merge the info in accountState and this orderbook
            _accounts.ClearREOwned();

            FunctionResult res;
            foreach (BrokerPosition p in accountState.Positions)
            {
                _log.captureDebug("StatePosition : sym=" + p.Symbol + "dir=" + p.Direction + " size=" + p.Size + " entry date=" + p.EntryDate + " entry price=" + p.EntryPrice + " margin=" + p.Margin + " shorted cash=" + p.ShortedCash);
                //FIX ME - verify this order book contains a position record for the sym/dir
            }
            foreach (BrokerOrder o in accountState.PendingOrders)
            {
                _log.captureDebug("StateOrder : sym=" + o.OrderSymbol + " type=" + o.OrderType + " pid=" + o.PositionID + " oid=" + o.OrderId + " trans type=" + o.TransactionType);
                res = _accounts.SetBrokerOrder(o);
                if (res.Error) { return res; }
            }

            FunctionResult vres = _accounts.VerifyREOwned();
            if (vres.Error) { return vres; }
            return new FunctionResult();
        }

        public List<int> GetActiveAccountList()
        {
            List<int> l = new List<int>();
            foreach (int a in _accounts.Accounts.Keys)
            { l.Add(a); }
            return l;
        }

        public void LogOrderBook(string p)
        {
            _log.captureDebug("OrderBookData Dump : " + p);
            _accounts.LogData(_log);
        }
    }

    public class OandAPlugin : IService, IBarDataRetrieval, ITickRetrieval, IBroker
    {
        public OandAPlugin()
        {
            _fx_client = new fxClientWrapper(this);

            //System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.TextWriterTraceListener("c:\\Storage\\src\\RE-LogFiles\\broker_trace.log"));
            //System.Diagnostics.Trace.AutoFlush = true;
        }
        ~OandAPlugin() { }

        private int _main_thread_id;

        private int _fail_ticket_num = 1;

        private fxClientWrapper _fx_client;
        public fxClientWrapper fxClient { get { return (_fx_client); } }

        private ResponseProcessor _response_processor = null;
        public ResponseProcessor ResponseProcessor { get { return (_response_processor); } }

        private OAPluginOptions _opts = null;
        private ServiceConnectOptions _connected_as = ServiceConnectOptions.None;

        private List<int> _reconnect_account_ids = new List<int>();

        private OrderBook _orderbook = new OrderBook();
        public OrderBook OrderBook { get { return (_orderbook); } }


        private BrokerLog _log = new BrokerLog();
        public BrokerLog BrokerLog { get { return _log; } }

        private TickLog _tick_log = new TickLog();
        private PluginLog _history_log = new PluginLog();

        private void disconnectCleanup()
        {
            _log.captureDebug("disconnectCleanup() called.");

            //FIX ME <-- if/when RightEdge implements an event for connection state changes, the call to RE should go here...

            _reconnect_account_ids.Clear();

            //if thread is main thread, stop the processor
            if (Thread.CurrentThread.ManagedThreadId == _main_thread_id && _response_processor != null)
            {
                _reconnect_account_ids = _response_processor.GetActiveAccountResponders();
                _response_processor.Stop();
                _response_processor.ClearAccountResponders();
                _response_processor = null;
            }

            _fx_client.Disconnect();
        }

        #region RightEdge Interfaces
        #region IService Members
        #region support members
        public string ServiceName()
        {
            return "Custom OandA Plugin";
        }

        public string Author()
        {
            return "Mark Loftis";
        }

        public string Description()
        {
            return "My Custom OandA Plugin (hitorical & live data, & broker)";
        }

        public string CompanyName()
        {
            return "";
        }

        public string Version()
        {
            return "0.1a";
        }

        public string id()
        {
            return "{899A5E4E-A4FD-42b7-8FE6-59FA0E417535}";
        }

        // Server address to connect to is not required in this case.  If it
        // were, return true here to notify RightEdge that it will
        // need to prompt for this information.
        public bool NeedsServerAddress()
        {
            return false;
        }

        // Port number to connect to is not required in this case.  If it
        // were, return true here to notify RightEdge that it will
        // need to prompt for this information.
        public bool NeedsPort()
        {
            return false;
        }

        // Authentication is not required in this case.  If it
        // were, return true here to notify RightEdge that it will
        // need to prompt for this information.
        public bool NeedsAuthentication()
        {
            return true;
        }

        public bool SupportsMultipleInstances()
        {
            //	Return True if it is OK to create multiple instances of this plugin
            //	Return False if not (usually because the service only allows one connection from each client)
            return false;
        }

        // Get or set the server address if required.  It is
        // not needed in this example.
        public string ServerAddress { get { return null; } set { } }

        // Get or set the server port if required.  It is
        // not needed in this example.
        public int Port { get { return 0; } set { } }

        // Get or set the name portion of authentication if required.  It is
        // not needed in this example.
        private string _username = "";
        public string UserName { get { return (_username); } set { _username = value; } }

        // Get or set the password portion of authentication if required.  It is
        // not needed in this example.
        private string _password = "";
        public string Password { get { return (_password); } set { _password = value; } }


        // Set this to true if the service supports retrieving historical
        // bar data, otherwise false.
        public bool BarDataAvailable
        {
            get { return true; }
        }

        // Set this to true if the service supports retrieving real-time
        // tick data, otherwise false.
        public bool TickDataAvailable
        {
            get { return true; }
        }

        // Set this to true if the service supports interfacing
        // to a broker, otherwise false.
        public bool BrokerFunctionsAvailable
        {
            get { return true; }
        }

        // Returns the IBarDataRetrieval instance implemented
        // in this plugin.  If not supported, return null.
        public IBarDataRetrieval GetBarDataInterface()
        {
            return this;
        }

        // Returns the ITickRetrieval instance implemented
        // in this plugin.  If not supported, return null.
        public ITickRetrieval GetTickDataInterface()
        {
            return this;
        }

        // Returns the IBroker instance implemented
        // in this plugin.  If not supported, return null.
        public IBroker GetBrokerInterface()
        {
            return this;
        }

        public bool HasCustomSettings()
        {
            //	Return false if not using custom settings
            return true;
        }

        public bool ShowCustomSettingsForm(ref RightEdge.Common.SerializableDictionary<string, string> settings)
        {
            //	Return false if not using custom settings
            //	If using custom settings, show form.  If user cancels, return false.  If user accepts,
            //	update settings and return true.
            OAPluginOptions topts = new OAPluginOptions(_opts);
            topts.loadRESettings(settings);

            OandAPluginOptionsForm frm = new OandAPluginOptionsForm(topts);
            DialogResult res = frm.ShowDialog();
            if (res == DialogResult.Cancel)
            { return false; }

            _opts = frm.Opts;
            return _opts.saveRESettings(ref settings);
        }
        #endregion

        public bool Initialize(RightEdge.Common.SerializableDictionary<string, string> settings)
        {
            if (_opts == null) { _opts = new OAPluginOptions(); }

            bool r = _opts.loadRESettings(settings);

            if (!r) { _opts = new OAPluginOptions(); }

            _log.FileName = _opts.LogOptionsBroker.LogFileName;
            _log.LogErrors = _opts.LogOptionsBroker.LogErrors;
            _log.ShowErrors = _opts.LogOptionsBroker.ShowErrors;
            _log.LogDebug = _opts.LogOptionsBroker.LogDebug;
            _log.LogExceptions = _opts.LogOptionsBroker.LogExceptions;

            _tick_log.FileName = _opts.LogOptionsTick.LogFileName;
            _tick_log.LogErrors = _opts.LogOptionsTick.LogErrors;
            _tick_log.ShowErrors = _opts.LogOptionsTick.ShowErrors;
            _tick_log.LogDebug = _opts.LogOptionsTick.LogDebug;
            _tick_log.LogExceptions = _opts.LogOptionsTick.LogExceptions;

            _history_log.FileName = _opts.LogOptionsHistory.LogFileName;
            _history_log.LogErrors = _opts.LogOptionsHistory.LogErrors;
            _history_log.ShowErrors = _opts.LogOptionsHistory.ShowErrors;
            _history_log.LogDebug = _opts.LogOptionsHistory.LogDebug;
            _history_log.LogExceptions = _opts.LogOptionsHistory.LogExceptions;

            _log.LogReceiveOA = _opts.LogOandaReceive;
            _log.LogReceiveRE = _opts.LogRightEdgeReceive;
            _log.LogSendOA = _opts.LogOandaSend;
            _log.LogSendRE = _opts.LogRightEdgeSend;

            _log.LogUnknownEvents = _opts.LogUnknownEvents;

            _tick_log.LogTicks = _opts.LogTicks;

            _orderbook.OAPluginOptions = _opts;
            _orderbook.OAPlugin = this;
            _orderbook.BrokerLog = _log;

            return r;
        }


        // Implements connection to a service functionality.
        // RightEdge will call this function before requesting
        // service data.  Return true if the connection is
        // successful, otherwise, false.
        public bool Connect(ServiceConnectOptions connectOptions)
        {
            clearError();

            PluginLog conlog = null;
            switch (connectOptions)
            {
                case ServiceConnectOptions.Broker: conlog = _log; break;
                case ServiceConnectOptions.HistoricalData: conlog = _history_log; break;
                case ServiceConnectOptions.LiveData: conlog = _tick_log; break;
            }
            if (conlog == null)
            { return false; }

            _main_thread_id = Thread.CurrentThread.ManagedThreadId;

            if (_fx_client.OAPluginOptions == null) { _fx_client.OAPluginOptions = _opts; }
            if (_fx_client.Log == null) { _fx_client.Log = conlog; }

            FXClientResult cres = _fx_client.Connect(connectOptions, _username, _password);
            if (cres.Error)
            {
                _fx_client.Disconnect();
                conlog.captureError(cres.Message, "Connect Error");
                return false;
            }

            if (connectOptions == ServiceConnectOptions.Broker)
            {
                if (_orderbook.OAPlugin == null) { _orderbook.OAPlugin = this; }
                if (_orderbook.BrokerLog == null) { _orderbook.BrokerLog = _log; }
                if (_fx_client.BrokerLog == null) { _fx_client.BrokerLog = _log; }

                //this is a broker connect, start up the response processor
                if (_response_processor == null)
                {
                    _response_processor = new ResponseProcessor(this, _log);
                    _response_processor.Start();
                }

                if (_reconnect_account_ids.Count > 0)
                {
                    foreach (int aid in _reconnect_account_ids)
                    {
                        conlog.captureDebug("Connect re-establishing account listener for account '" + aid + "'.");

                        TaskResult tres = _response_processor.ActivateAccountResponder(aid);
                        if (tres.Error)
                        {
                            _fx_client.Disconnect();
                            conlog.captureError(tres.Message, "Connect Error");

                            //remove un-added aid from _handling list so it gets 'skipped' next time
                            if (!tres.TaskCompleted) { _reconnect_account_ids.Remove(aid); }
                            return false;
                        }
                    }
                }
            }

            _connected_as = connectOptions;
            return true;
        }

        // Implements disconnection from a service.
        // RightEdge will call this function before ending
        // data requests.  Return true if the disconnection is
        // successful, otherwise, false.
        public bool Disconnect()
        {
            clearError();
            if (_connected_as == ServiceConnectOptions.Broker)
            {
                _log.captureDebug("Disconnect() called.");
                if (_response_processor != null)
                {
                    _response_processor.Stop();
                    _response_processor = null;
                }

                _orderbook.LogOrderBook("Disconnecting");

                if (!string.IsNullOrEmpty(_opts.OrderLogFileName))
                {
                    FunctionResult sres=_orderbook.saveSettings();
                    if (sres.Error)
                    {
                        _log.captureError(sres.Message, "Disconnect Error");
                        return false;
                    }
                }
            }

            FXClientResult res = _fx_client.Disconnect();
            if (res.Error)
            {
                _log.captureError(res.Message, "Disconnect Error");
                return false;
            }
            return true;
        }

        private string _error_str = string.Empty;
        public void clearError() { _error_str = string.Empty; }
        // Return the last error encountered by the service.
        public string GetError()
        {
            return (_error_str);
        }
        #endregion

        #region IBarDataRetrieval Members

        // Supports a list of frequencies that this retrieval
        // service supports.  In the case of Yahoo, only daily
        // bars are supported and that is returned in the list.
        public List<int> GetAvailableFrequencies()
        {
            return OandAUtils.supportedIntervals();
        }

        // This function is called to finally retrieve the data from
        // source.
        public List<BarData> RetrieveData(Symbol symbol, int frequency, DateTime startDate, DateTime endDate, BarConstructionType barConstruction)
        {
            try
            {
                int num_ticks = 500;
                clearError();

                Interval interval = OandAUtils.convertToInterval(frequency);
                CustomBarFrequency cbf = OandAUtils.convertToCustomBarFrequency(frequency);

                //calculate available end date based on Now() and 500 bars@interval for the start
                DateTime availableEnd = OandAUtils.frequencyRoundToStart(DateTime.UtcNow, cbf);
                DateTime availableStart = OandAUtils.frequencyIncrementTime(availableEnd, cbf, num_ticks * -1);

                //validate the input date range overlaps the available range
                if (startDate > availableEnd || endDate < availableStart)
                {
                    _error_str = "No data available for the requested time period.";
                    _history_log.captureError(_error_str, "RetrieveData Error");
                    return null;
                }

                List<BarData> list = new List<BarData>();
                FXClientObjectResult<ArrayList> hal = _fx_client.GetHistory(new fxPair(symbol.Name), interval, num_ticks, true);
                if (hal.Error)
                {
                    if (hal.Disconnected)
                    {
                        _error_str = "Disconnected : " + hal.Message;
                        _history_log.captureError(_error_str, "RetrieveData Error");
                        _fx_client.Disconnect();
                        return null;
                    }
                    _error_str = hal.Message;
                    _history_log.captureError(_error_str, "RetrieveData Error");
                    return null;
                }

                DataFilterType filter_type = _opts.DataFilterType;
                DayOfWeek weekend_start_day = _opts.WeekendStartDay;
                DayOfWeek weekend_end_day = _opts.WeekendEndDay;
                TimeSpan weekend_start_time = _opts.WeekendStartTime;
                TimeSpan weekend_end_time = _opts.WeekendEndTime;
                bool drop_bar;

                System.Collections.IEnumerator iEnum = hal.ResultObject.GetEnumerator();
                while (iEnum.MoveNext())
                {
                    fxHistoryPoint hp = (fxHistoryPoint)iEnum.Current;
                    DateTime hpts = hp.Timestamp;

                    if (hpts < startDate) { continue; }

                    drop_bar = false;
                    switch (filter_type)
                    {
                        case DataFilterType.WeekendTimeFrame:
                            if (hpts.DayOfWeek >= weekend_start_day || hpts.DayOfWeek <= weekend_end_day)
                            {
                                drop_bar = true;

                                if (hpts.DayOfWeek == weekend_start_day && hpts.TimeOfDay < weekend_start_time)
                                { drop_bar = false; }

                                if (hpts.DayOfWeek == weekend_end_day && hpts.TimeOfDay >= weekend_end_time)
                                { drop_bar = false; }
                            }
                            break;
                        case DataFilterType.PriceActivity:
                            CandlePoint cp = hp.GetCandlePoint();
                            double n = cp.Open;
                            if (n == cp.Close && n == cp.Min && n == cp.Max)
                            { drop_bar = true; }
                            break;
                        case DataFilterType.None:
                            break;
                        default:
                            _error_str = "Unknown Data Filter Type setting '" + filter_type + "'.";
                            _history_log.captureError(_error_str, "RetrieveData Error");
                            return null;
                    }
                    if (drop_bar) { continue; }

                    if (hpts > endDate) { break; }

                    list.Add(OandAUtils.convertBarData(hp));
                }

                return list;
            }
            catch (Exception e)
            {
                _history_log.captureException(e);
                _error_str = "Unhandled exception : " + e.Message;
                _history_log.captureError(_error_str, "RetrieveData Error");
                return null;
            }
        }

        // Retrieves this IService instance.
        public IService GetService()
        {
            return this;
        }

        #endregion

        #region ITickRetrieval Members

        // Specifies whether or not this is a real time
        // tick data retrieval plugin.
        public bool RealTimeDataAvailable
        {
            get
            {
                return true;
            }
        }

        private GotTickData _gtd_event;

        // This is called from RightEdge to assign any listeners
        // to this service to retrieve incoming ticks.
        public GotTickData TickListener
        {
            set
            {
                _gtd_event = value;
            }
        }
        private void fireTickEvent(Symbol sym, TickData tick)
        {
            _tick_log.captureTick(sym, tick);
            _gtd_event(sym, tick);
        }

        private List<RateTicker> _rate_tickers = new List<RateTicker>();
        public List<RateTicker> RateTickers { get { return (_rate_tickers); } }

        public void handleRateTicker(RateTicker rt, fxRateEventInfo ei, fxEventManager em)
        {
            try
            {
                TickData bid_tick = OandAUtils.convertTicks_bid(ei.Tick);
                fireTickEvent(rt.Symbol, bid_tick);

                TickData ask_tick = OandAUtils.convertTicks_ask(ei.Tick);
                fireTickEvent(rt.Symbol, ask_tick);

                if (ei.Tick.Ask > rt.High)
                {
                    rt.High = ei.Tick.Ask;
                    TickData tick = OandAUtils.convertTicks(ei.Tick);
                    tick.tickType = TickType.HighPrice;
                    tick.price = rt.High;
                    fireTickEvent(rt.Symbol, tick);
                }
                if (ei.Tick.Bid < rt.Low)
                {
                    rt.Low = ei.Tick.Bid;
                    TickData tick = OandAUtils.convertTicks(ei.Tick);
                    tick.tickType = TickType.LowPrice;
                    tick.price = rt.Low;
                    fireTickEvent(rt.Symbol, tick);
                }

                TickData volume_tick = OandAUtils.convertTicks(ei.Tick);
                volume_tick.tickType = TickType.DailyVolume;
                volume_tick.size = (ulong)rt.TickCount;
                //volume_tick.price = (double)rt.TickCount;
                fireTickEvent(rt.Symbol, volume_tick);

                TickData trade_tick = OandAUtils.convertTicks_trade(ei.Tick);
                fireTickEvent(rt.Symbol, trade_tick);
            }
            catch (Exception e)
            {
                _tick_log.captureException(e);
                _error_str = "Unhandled exception : " + e.Message;
                _tick_log.captureError(_error_str, "RetrieveData Error");
                throw new OAPluginException(_error_str, e);
            }

        }

        // This is called by RightEdge to set the symbol list
        // that is requested by the user.
        public bool SetWatchedSymbols(List<Symbol> symbols)
        {
            FXClientResult res = _fx_client.SetWatchedSymbols(_rate_tickers, symbols, this);
            if (res.Error)
            {
                if (res.Disconnected)
                {
                    _error_str = "Disconnected : " + res.Message;
                    _fx_client.Disconnect();
                }
                else
                {
                    _error_str = res.Message;
                }
                _tick_log.captureError(_error_str, "SetWatchedSymbols Error");
                return false;
            }
            return true;
        }

        private bool _is_watching = false;

        public bool IsWatching()
        {
            // Return the state of the service.  If it is currently
            // listening/watching for ticks, return true.
            return _is_watching;
        }

        public bool StartWatching()
        {
            // Called by RightEdge to initiate the data watch.
            _is_watching = true;
            return _is_watching;
        }

        public bool StopWatching()
        {
            // Called by RightEdge to stop watching/listening for data.
            _is_watching = false;
            return _is_watching;
        }
        #endregion

        #region IBroker Members
        #region broker properties, events and state
        // These events are fired when the appropriate actions occur.  These events
        // are subscribed to by the RightEdge user interface to update the user
        // when something of note happens on the broker.
        public event OrderUpdatedDelegate OrderUpdated;
        public event PositionAvailableDelegate PositionAvailable;

        // Return true if this is a broker plugin that connects to a live broker.
        // This should be set to true even if the broker supports a demo mode.
        public bool IsLiveBroker()
        {
            return true;
        }

        // This function is called before Connect to notify the broker
        // of the list of orders that the system expects are pending,
        // and the positions it expects are open.
        public void SetAccountState(BrokerAccountState accountState)
        {
            _log.captureDebug("SetAccountState() called.");
            
            if (accountState.PendingOrders.Count == 0 && accountState.Positions.Count == 0)
            {//nothing from RE so skip loading the orderbook & just start fresh
                return;
            }

            if (string.IsNullOrEmpty(_opts.OrderLogFileName))
            {//the plugin doesn't know anything about what might be in accountState
                if (accountState.PendingOrders.Count != 0 || accountState.Positions.Count != 0)
                {
                    _error_str="There is no order log from the broker. Unable to reconnect position state to the transactions at oanda";
                    _log.captureError(_error_str,"SetAccountState Error");
                    return;
                }
                return;
            }
            FileInfo fi = new FileInfo(_opts.OrderLogFileName);
            if (!fi.Exists || fi.Length == 0)
            {//the plugin doesn't know anything about what might be in accountState
                if (accountState.PendingOrders.Count != 0 || accountState.Positions.Count != 0)
                {
                    _error_str = "There is no order log from the broker. Unable to reconnect position state to the transactions at oanda";
                    _log.captureError(_error_str, "SetAccountState Error");
                    return;
                }
                return;
            }

            FunctionResult obres = _orderbook.loadSettings(_opts.OrderLogFileName);
            if(obres.Error)
            {
                _error_str = obres.Message;
                _log.captureError(_error_str, "SetAccountState Error");
                return;
            }

            #region manual LiveOpenPosition.xml parsing - need for version < RE 2008.1.381
            /*
            /////////////////////////////////////////////////////////////////////////////////
            //ok there are orders in accountState and the saved orderbook has been loaded.
            //untill the accountState issue is resolved, manually load the LiveOpenPositions.xml
            //and finish filling out the accountState
            MessageBox.Show("Select the current systems LiveOpenPositions.xml file.");
            OpenFileDialog fd = new OpenFileDialog();
            fd.Title = "Select LiveOpenPositions.xml";
            fd.DefaultExt = "xml";
            fd.Filter = "Xml data files (*.xml)|*.xml";
            fd.CheckPathExists = true;
            fd.CheckFileExists = true;
            fd.Multiselect = false;
            DialogResult dres = fd.ShowDialog();
            if (dres == DialogResult.OK)
            {
                _log.captureDebug("System LiveOpenPositions.xml file name : '" + fd.FileName + "'.");
                string fname = fd.FileName;
                fd.Dispose();
                FunctionObjectResult<PortfolioXml> pres = PortfolioXml.newFromSettings<PortfolioXml>(fname);
                if (pres.Error)
                {
                    _error_str = obres.Message;
                    _log.captureError(_error_str, "SetAccountState Error");
                    return;
                }
                PortfolioXml saved_portfolio = pres.ResultObject;

                //flush out accountState using the saved_portfolio
                bool item_found = false;
                bool all_items_found = true;
                foreach (BrokerOrder bo in accountState.PendingOrders)
                {
                    item_found = false;
                    foreach (PositionDataXml pd in saved_portfolio.Positions)
                    {//find bo.OrderID in saved_portfolio.Positions
                        if (item_found) { break; }
                        foreach (TradeOrderXml tox in pd.PendingOrders)
                        {
                            if (tox.OrderID == bo.OrderId)
                            {
                                bo.PositionID = pd.PosID;
                                //<---- any other missing fields?? grab the data here

                                item_found = true;
                                break;
                            }
                        }
                        if (!item_found)
                        {
                            foreach (TradeInfo ti in pd.Trades)
                            {
                                if (ti.OrderID == bo.OrderId)
                                {
                                    bo.PositionID = pd.PosID;
                                    //<---- any other missing fields?? grab the data here

                                    item_found = true;
                                    break;
                                }
                            }
                        }
                    }
                    if (!item_found) { all_items_found = false; }
                }
                if (!all_items_found)
                {
                    _error_str = "Unable to reconcile the accountState with the LiveOpenPositions.xml";
                    _log.captureError(_error_str, "SetAccountState Error");
                    return;
                }
            }
            /////////////////////////////////////////////////////////////////////////////////
            */
#endregion

            FunctionResult sres=_orderbook.SetAccountState(accountState);
            if (sres.Error)
            {
                _error_str = sres.Message;
                _log.captureError(_error_str, "SetAccountState Error");
                return;
            }
            
            _reconnect_account_ids = _orderbook.GetActiveAccountList();
            return;
        }
        #endregion



        #region orderbok operations
        private bool submitOrderHandleResult(BrokerOrder order, IFXClientResponse r, string act_id, bool added, out string orderId)
        {
            if (r.Error)
            {
                if (r.FXClientResponse == FXClientResponseType.Disconnected || r.FXClientResponse == FXClientResponseType.Rejected)
                {
                    rejectOrder(order, out orderId);
                }
                else if (r.FXClientResponse == FXClientResponseType.Invalid)
                {
                    invalidateOrder(order, out orderId);
                }

                setResponseErrorMessage(r);

                _log.captureError(_error_str, "SubmitOrder Error");

                if (added && r.OrdersSent == 0) { _response_processor.DeactivateAccountResponder(int.Parse(act_id)); }
                orderId = order.OrderId;
                return false;
            }
            orderId = order.OrderId;
            return true;
        }
        private void setResponseErrorMessage(IFXClientResponse r)
        {
            if (r.Disconnected)
            {
                _error_str = "Disconnected : " + r.Message;
                disconnectCleanup();
            }
            else
            {
                _error_str = r.Message;
            }
        }
        private void returnOrder(FXClientResponseType rt, BrokerOrder order, out string orderId)
        {
            if (rt == FXClientResponseType.Rejected || rt == FXClientResponseType.Disconnected)
            { invalidateOrder(order, out orderId); }
            else if (rt == FXClientResponseType.Invalid)
            { rejectOrder(order, out orderId); }
            else
            {
                throw new Exception("can't return an order in accepted state!");
            }
        }




        public bool SubmitOrder(BrokerOrder order, out string orderId)
        {
            clearError();
            orderId = string.Empty;
            _log.captureDebug("SubmitOrder() called.");
            _log.captureREIn(order);

            if (!_fx_client.IsInit)//the fx_client is gone, there was a disconnection event...
            {//sometimes RE acts again before checking the error from the last action
                rejectOrder(order, out orderId);
                _error_str = "Disconnected : Broker not connected!";
                _log.captureError(_error_str, "SubmitOrder Error");
                return false;//this should re-trigger the disconnect logic in RE
            }

            string act_id=null;
            FXClientTaskResult tres = null;
            try
            {
                TransactionType ott = order.TransactionType;
                switch (order.OrderType)
                {
                    case OrderType.Market:
                        #region market order
                        if (ott == TransactionType.Sell || ott == TransactionType.Cover)
                        {
                            tres = _orderbook.SubmitCloseOrder(order, out act_id);
                            return submitOrderHandleResult(order, tres, act_id, tres.TaskCompleted, out orderId);
                        }
                        else if (ott == TransactionType.Buy || ott == TransactionType.Short)
                        {
                            tres = _orderbook.SubmitMarketOrder(order, out act_id);
                            return submitOrderHandleResult(order, tres, act_id, tres.TaskCompleted, out orderId);
                        }
                        else
                        {
                            rejectOrder(order, out orderId);
                            _error_str = "Unknown market order transaction type '" + ott + "'.";
                            _log.captureError(_error_str, "SubmitOrder Error");
                            return false;
                        }
                        #endregion
                    case OrderType.Limit:
                        #region limit order
                        if (order.TransactionType == TransactionType.Sell || order.TransactionType == TransactionType.Cover)
                        {
                            tres = _orderbook.SubmitPositionTargetProfitOrder(order, out act_id);
                            return submitOrderHandleResult(order, tres, act_id, tres.TaskCompleted, out orderId);
                        }
                        else if (ott == TransactionType.Buy || ott == TransactionType.Short)
                        {
                            tres = _orderbook.SubmitLimitOrder(order, out act_id);
                            return submitOrderHandleResult(order, tres, act_id, tres.TaskCompleted, out orderId);
                        }
                        else
                        {
                            rejectOrder(order, out orderId);
                            _error_str = "Unknown limit order transaction type '" + ott + "'.";
                            _log.captureError(_error_str, "SubmitOrder Error");
                            return false;
                        }
                        #endregion
                    case OrderType.Stop:
                        #region stop order
                        if (order.TransactionType == TransactionType.Sell || order.TransactionType == TransactionType.Cover)
                        {
                            tres = _orderbook.SubmitPositionStopLossOrder(order,out act_id);
                            return submitOrderHandleResult(order, tres, act_id, tres.TaskCompleted, out orderId);
                        }
                        else
                        {
                            rejectOrder(order, out orderId);
                            _error_str = "Unknown stop order transaction type '" + ott + "'.";
                            _log.captureError(_error_str, "SubmitOrder Error");
                            return false;
                        }
                        #endregion
                    default:
                        rejectOrder(order, out orderId);
                        _error_str = "Unknown order type '" + order.OrderType + "'.";
                        _log.captureError(_error_str, "SubmitOrder Error");
                        return false;
                }//end of switch()
            }//end of try{}
            catch (Exception e)
            {
                rejectOrder(order, out orderId);
                _log.captureException(e);
                _error_str = "Unhandled exception : " + e.Message;
                _log.captureError(_error_str, "SubmitOrder Error");
                if (tres != null && tres.TaskCompleted && act_id != null) { _response_processor.DeactivateAccountResponder(int.Parse(act_id)); }
                return false;
            }
        }

        // Clear all open orders that haven't been processed yet
        public bool CancelAllOrders()
        {
            clearError();
            _log.captureDebug("CancelAllOrders() called.");
            _log.captureREIn("CANCEL ALL");

            if (!_fx_client.IsInit)//the fx_client is gone, there was a disconnection event...
            {//sometimes RE acts again before checking the error from the last action
                _error_str = "Disconnected : Broker not connected!";
                _log.captureError(_error_str, "CancelAllOrders Error");
                return false;//this should re-trigger the disconnect logic in RE
            }

            try
            {
                FXClientResult res = _orderbook.CancelAllOrders();
                if (res.Error)
                {
                    setResponseErrorMessage(res);
                    _log.captureError(_error_str, "CancelAllOrders Error");
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                _log.captureException(e);
                _error_str = "Unhandled exception : " + e.Message;
                _log.captureError(_error_str, "CancelAllOrders Error");
                return false;
            }
        }

        // Cancel or clear a particular order if it hasn't been processed yet.
        public bool CancelOrder(string orderId)
        {
            clearError();
            _log.captureDebug("CancelOrder() called : id='" + orderId + "'");
            _log.captureREIn("CANCEL ORDER '" + orderId + "'");

            if (!_fx_client.IsInit)//the fx_client is gone, there was a disconnection event...
            {//sometimes RE acts again before checking the error from the last action
                _error_str = "Disconnected : Broker not connected!";
                _log.captureError(_error_str, "CancelOrder Error");
                return false;//this should re-trigger the disconnect logic in RE
            }

            try
            {
                IDString oid = new IDString(orderId);
                int id_num = oid.Num;

                bool is_pos_id = false;
                if (oid.Type != IDType.Other) { is_pos_id = true; }

                FXClientResult res;
                if (is_pos_id)
                {
                    res = _orderbook.CancelPositionOrder(oid);
                }
                else
                {
                    res = _orderbook.CancelSpecificOrder(oid);
                }
                if (res.Error)
                {
                    setResponseErrorMessage(res);
                    _log.captureError(_error_str, "CancelOrder Error");
                    return false;
                }
                return true;
            }//end of try{}
            catch (Exception e)
            {
                _log.captureException(e);
                _error_str = "Unhandled exception : " + e.Message;
                _log.captureError(_error_str, "CancelOrder Error");
                return false;
            }
        }
        #endregion

        #region account status
        // Informs RightEdge of the amount available for buying or shorting.
        public double GetBuyingPower()
        {
            clearError();
            _log.captureDebug("GetBuyingPower() called.");

            if (!_fx_client.IsInit)//the fx_client is gone, there was a disconnection event...
            {//sometimes RE acts again before checking the error from the last action
                _error_str = "Disconnected : Broker not connected!";
                _log.captureError(_error_str, "GetBuyingPower Error");
                return -1.0;//this should re-trigger the disconnect logic in RE
            }


            try
            {
                FXClientObjectResult<double> res = _orderbook.GetBuyingPower();
                if (res.Error)
                {
                    setResponseErrorMessage(res);
                    _log.captureError(_error_str, "GetBuyingPower Error");
                    return -1.0;
                }

                return res.ResultObject;
            }
            catch (Exception e)
            {
                _log.captureException(e);
                _error_str = "Unhandled exception : " + e.Message;
                _log.captureError(_error_str, "GetBuyingPower Error");
                return -1.0;
            }
        }

        public double GetMargin()
        {
            clearError();
            _log.captureDebug("GetMargin() called.");

            if (!_fx_client.IsInit)//the fx_client is gone, there was a disconnection event...
            {//sometimes RE acts again before checking the error from the last action
                _error_str = "Disconnected : Broker not connected!";
                _log.captureError(_error_str, "GetMargin Error");
                return -1.0;//this should re-trigger the disconnect logic in RE
            }

            try
            {
                FXClientObjectResult<double> res = _orderbook.GetMarginAvailable();
                if (res.Error)
                {
                    setResponseErrorMessage(res);
                    _log.captureError(_error_str, "GetMargin Error");
                    return -1.0;
                }

                return res.ResultObject;
            }
            catch (Exception e)
            {
                _log.captureException(e);
                _error_str = "Unhandled exception : " + e.Message;
                _log.captureError(_error_str, "GetMargin Error");
                return -1.0;
            }
        }

        public double GetShortedCash()
        {
            clearError();
            _log.captureDebug("GetShortedCash() called.");
            //	Return the amount of cash received from shorting stock
            return 0.0;
        }
        #endregion

        #region orderbook status functions
        // Returns an order with the specified id
        public BrokerOrder GetOpenOrder(string id)
        {
            try
            {
                clearError();
                _log.captureDebug("GetOpenOrder(id='" + id + "') called.");

                BrokerOrder bo = _orderbook.GetOpenBrokerOrder(id);
                if (bo == null)
                {
                    _error_str = "Unable to locate an open order record for id : '" + id + "'.";
                    return null;
                }
                return bo;
            }
            catch (Exception e)
            {
                _log.captureException(e);
                _error_str = "Unhandled exception : " + e.Message;
                _log.captureError(_error_str, "GetOpenOrder Error");
                return null;
            }
        }

        // returns a copy of the currently open orders.
        public List<BrokerOrder> GetOpenOrders()
        {
            try
            {
                clearError();
                _log.captureDebug("GetOpenOrders() called.");

                return _orderbook.GetOpenBrokerOrderList();
            }
            catch (Exception e)
            {
                _log.captureException(e);
                _error_str = "Unhandled exception : " + e.Message;
                _log.captureError(_error_str, "GetOpenOrders Error");
                return null;
            }
        }

        public int GetShares(Symbol symbol)
        {
            try
            {
                clearError();
                _log.captureDebug("GetShares(symbol='" + symbol.Name + "') called.");

                FunctionObjectResult<int> sres = _orderbook.GetSymbolShares(symbol.Name);
                if (sres.Error)
                {
                    _error_str = sres.Message;
                    _log.captureError(_error_str, "GetShares Error");
                    return 0;
                }
                return sres.ResultObject;
            }
            catch (Exception e)
            {
                _log.captureException(e);
                _error_str = "Unhandled exception : " + e.Message;
                _log.captureError(_error_str, "GetShares Error");
                return 0;
            }
        }
        #endregion

        #region Event delegate add/remove member functions
        // These events are fired when the appropriate actions occur.  These events
        // are subscribed to by the RightEdge user interface to update the user
        // when something of note happens on the broker.
        public void AddOrderUpdatedDelegate(OrderUpdatedDelegate orderUpdated)
        {
            OrderUpdated += orderUpdated;
        }

        public void RemoveOrderUpdatedDelegate(OrderUpdatedDelegate orderUpdated)
        {
            OrderUpdated -= orderUpdated;
        }

        public void AddPositionAvailableDelegate(PositionAvailableDelegate positionAvailable)
        {
            PositionAvailable += positionAvailable;
        }

        public void RemovePositionAvailableDelegate(PositionAvailableDelegate positionAvailable)
        {
            PositionAvailable -= positionAvailable;
        }
        #endregion
        #endregion
        #endregion

        #region IDisposable Members
        // Must be implemented, however, action is
        // not required as is the case here.
        public void Dispose()
        {
        }
        #endregion

        #region logged "send to RightEdge" wrappers
        public void FireOrderUpdated(BrokerOrder order, Fill fill, string s)
        {
            _log.captureREOut(order, fill, "SEND RE (" + s + ")");
            OrderUpdated(order, fill, s);
        }
        public BrokerOrderState RESendFilledOrder(Fill fill, BrokerOrder order, string s)
        {
            order.Fills.Add(fill);

            int tq = 0;
            foreach (Fill f in order.Fills)
            { tq += f.Quantity; }

            if (tq >= order.Shares) { order.OrderState = BrokerOrderState.Filled; }
            else { order.OrderState = BrokerOrderState.PartiallyFilled; }

            FireOrderUpdated(order, fill, "fill on " + s);
            return order.OrderState;
        }
        public void RESendNoMatch(ResponseRecord resp)
        {
            //this receives ALL ACCOUNT EVENTS!!! It doesn't matter where or how they originate.
            //If ANY connected client triggers an event, it will be sent to ALL clients
            _log.captureUnknownEvent(resp.Transaction, resp.AccountId);

        }
        private void invalidateOrder(BrokerOrder order, out string orderId)
        {//order (or a plugin generated sub-order of order) was explicity invalidated by an oanda account or order exception
            if (_opts.LogTradeErrors)
            {
                string ostr = "ORDER INVALID : OrderID='" + order.OrderId + "' PosID='" + order.PositionID + "' Symbol='" + order.OrderSymbol.Name + "' Shares='" + order.Shares + "' Transaction='" + order.TransactionType + "' Type='" + order.OrderType + "' State='" + order.OrderState + "'.";
                _log.captureError(ostr, "Order Invalid");
            }

            if (string.IsNullOrEmpty(order.OrderId))
            {//create one now...
                IDString ids = new IDString(IDType.Fail, _fail_ticket_num++);
                order.OrderId = ids.ID;
            }
            orderId = order.OrderId;
            order.OrderState = BrokerOrderState.Invalid;
            FireOrderUpdated(order, null, "invalid order");
            return;
        }
        private void rejectOrder(BrokerOrder order, out string orderId)
        {//order (or a plugin generated sub-order of order) encountered a critical failure, not related to execution at oanda.
            if (_opts.LogTradeErrors)
            {
                string ostr = "ORDER REJECTED : OrderID='" + order.OrderId + "' PosID='" + order.PositionID + "' Symbol='" + order.OrderSymbol.Name + "' Shares='" + order.Shares + "' Transaction='" + order.TransactionType + "' Type='" + order.OrderType + "' State='" + order.OrderState + "'.";
                _log.captureError(ostr, "Order Rejected");
            }

            if (string.IsNullOrEmpty(order.OrderId))
            {//create one now...
                IDString ids = new IDString(IDType.Fail, _fail_ticket_num++);
                order.OrderId = ids.ID;
            }
            orderId = order.OrderId;
            order.OrderState = BrokerOrderState.Rejected;
            FireOrderUpdated(order, null, "rejected order");
            return;
        }
        #endregion
    }
}
