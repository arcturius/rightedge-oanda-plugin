using System;
using System.Runtime.Remoting.Contexts;
using System.Drawing.Design;
using System.ComponentModel;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;
using System.Runtime.Serialization;

using RightEdge.Common;

using fxClientAPI;


/* TODO
BUGS:
 * if the fxclient wrapper functions which take an account object have to call connectOut()
   they also need to re-fetch the account object, since it would have come from the previous connection...
   right now those calls will probably end up throwing an "object disposed" exception

FEATURE FIXES:
  * orders price bounds
   * currently using limitprice, but this conflicts with some order types
     * need a better way to transport the bound price point
   * currently bounds range is a plugin option only
     * with a better transport mechanism this could be on a per order basis.
   * what event does oanda fire if a (market/limit) order is rejected due to a bounds violation?
 
 * fix account handling
    * curently using the exchange field as the transport
      * for getting an order specific account
    * fix & verify all broker accont status functions
    * etc...
  
IMPROVEMENTS:
 * need some sort of "internal fullfilment" mechanism to match long/short orders
   since oanda only allows one-way positions
   * would require processing the trans link chain and some new trans types
     which would further prep the code for full 2-way synch with
     external fxTrade GUI events received and handled for all orders under RE's domain.

 * don't include the whole broker record when serializing the orderbook
   * will require exposing certain broker record members in the order record space
     since they are usefull to persist.
   * there is some duplication in the BrokerPositionRecord (as it is a RightEdge BrokerPosition derivative)
*/


namespace RightEdgeOandaPlugin
{
    #region XML Dictionary Serialization
    /// http://weblogs.asp.net/pwelter34/archive/2006/05/03/444961.aspx
    /// Author : Paul Welter (pwelter34)
    [XmlRoot("dictionary")]
    public class SerializableDictionary<TKey, TValue>
        : Dictionary<TKey, TValue>, IXmlSerializable
    {
        public SerializableDictionary(){ }
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
    public class OAPluginException : Exception,ISerializable
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
        public FunctionResult(FunctionResult src) { Error = src.Error; _message = src.Message; }

        public bool Error = false;

        private string _message = string.Empty;
        public string Message { set { _message = value; } get { return (_message); } }

        public void setError(string m) { _message = m; Error = true; }
        public void clearError() { _message = string.Empty; Error = false; }
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

        //FIX ME - finish this up...
        public static double determineRate(fxClient fx_client, bool is_buy, DateTime timestamp, string base_cur, string quote_cur)
        {
            string b_sym = base_cur + "/" + quote_cur;
            fxPair b_pair = new fxPair(b_sym);

            TimeSpan time_to_order = DateTime.UtcNow.Subtract(timestamp);
            int ticks_to_trans = (time_to_order.Seconds % 5) + 1;

            fxTick b_tick = fx_client.RateTable.GetRate(b_pair);

            //array of fxHistoryPoints
            ArrayList b_rates = fx_client.RateTable.GetHistory(b_pair, Interval.Every_5_Seconds, ticks_to_trans);

            //find in b_rates index of tick with timestamp that contains the transaction timestamp
            int b_rate_i = 0;

            //double b_rate_avg_pr;
            //double b_tick_pr;

            fxHistoryPoint b_rate = (fxHistoryPoint)b_rates[b_rate_i];
            if (is_buy)
            {

                //b_rate_avg_pr=;

                //b_tick_pr=;
            }
            else
            {
                // b_rate_avg_pr=;
                //b_tick_pr=;
            }
            //log b pair, tick, avg,i and rates[i]
            return 0.0;
        }

        public static Fill generateFillFromTransaction(fxClient fx_client, Transaction t, string base_currency)
        {
            Fill fill = new Fill();
            fill.FillDateTime = t.Timestamp;

            double sym_pr = t.Price;
            double act_pr = 1.0;

            // Symbol : Base/Quote
            if (t.Base == base_currency)
            {
                act_pr = 1.0 / sym_pr;
            }
            else if (t.Quote != base_currency)
            {//neither the Base nor the Quote is the base_currency

                //determine a Base/base_currency and/or Quote/base_currency cross price factor
                double b_pr = OandAUtils.determineRate(fx_client, t.IsBuy(), t.Timestamp, t.Base, base_currency);
                double q_pr = OandAUtils.determineRate(fx_client, t.IsBuy(), t.Timestamp, t.Quote, base_currency);

                //FIX ME - convert act_pr using the cross factor

            }
            //else (t.Quote == base_currency)

            fill.Price = new Price(sym_pr, act_pr);

            fill.Quantity = (t.Units < 0) ? (-1 * t.Units) : t.Units;
            return (fill);
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
    }
    #endregion

    public class PluginLog
    {
        public PluginLog() { }

        ~PluginLog() { closeLog(); }

        private bool _log_exceptions = true;
        public bool LogExceptions { set { _log_exceptions = value; } get { return (_log_exceptions); } }

        private bool _log_errors = true;
        public bool LogErrors { set { _log_errors = value; } get { return (_log_errors); } }

        private bool _log_debug = true;
        public bool LogDebug { set { _log_debug = value; } get { return (_log_debug); } }

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

        private bool _show_errors = true;
        public bool ShowErrors { set { _show_errors = value; } get { return (_show_errors); } }

        public bool IsOpen { get { return (_fs != null); } }

        private string _fname = null;
        public string FileName { get { return (_fname); } set { closeLog(); _fname = value; } }

        private FileStream _fs = null;

        private int _monitor_timeout = 1000;

        public void closeLog()
        {
            if (_fs != null) { _fs.Close(); _fs = null; }
        }
        public void openLog()
        {
            if (_fs != null) { throw new OAPluginException("Log already open."); }
            _fs = new FileStream(_fname, FileMode.Append, FileAccess.Write);
        }
        private void writeMessage(string message)
        {
            if (!IsOpen) { openLog(); }

            string msg = DateTime.Now.ToString() + " [" + Thread.CurrentThread.ManagedThreadId + "] : " + message + "\n";
            byte[] msg_bytes = new UTF8Encoding(true).GetBytes(msg);

            if (!Monitor.TryEnter(_fs, _monitor_timeout))
            {
                throw new OAPluginException("Unable to acquire lock on log file stream.");
            }

            try
            {
                _fs.Write(msg_bytes, 0, msg_bytes.Length);
                _fs.Flush();
            }
            catch (Exception e)
            {
                throw new OAPluginException("", e);
            }
            finally { Monitor.Pulse(_fs); Monitor.Exit(_fs); }
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

    public enum DataFilterType { None, WeekendTimeFrame, PriceActivity };

    [Serializable]
    public class OAPluginOptions
    {
        public OAPluginOptions() { }
        public OAPluginOptions(OAPluginOptions src) { Copy(src); }
        
        public void Copy(OAPluginOptions src)
        {
            OAPluginOptions rsrc = src;
            if (src == null) { rsrc = new OAPluginOptions(); }
            _data_filter_type = rsrc._data_filter_type;
            _use_bounds = rsrc._use_bounds;
            _log_errors = rsrc._log_errors;
            _log_trade_errors = rsrc._log_trade_errors;

            _log_re_in = rsrc._log_re_in;
            _log_re_out = rsrc._log_re_out;
            _log_oa_in = rsrc._log_oa_in;
            _log_oa_out = rsrc._log_oa_out;

            _order_log_fname = rsrc._order_log_fname;

            _log_fxclient = rsrc._log_fxclient;
            _fxclient_log_fname = rsrc._fxclient_log_fname;

            _log_debug = rsrc._log_debug;
            _log_fname = rsrc._log_fname;
            _log_ticks = rsrc._log_ticks;
            _tick_log_fname = rsrc._tick_log_fname;
            _opt_fname = rsrc._opt_fname;
            _show_errors = rsrc._show_errors;
            _log_unknown_events = rsrc._log_unknown_events;
            _use_game = rsrc._use_game;
            _weekend_end_day = rsrc._weekend_end_day;
            _weekend_end_time = rsrc._weekend_end_time;
            _weekend_start_day = rsrc._weekend_start_day;
            _weekend_start_time = rsrc._weekend_start_time;
        }

        #region RightEdge 'serialization'
        public bool loadRESettings(RightEdge.Common.SerializableDictionary<string, string> settings)
        {
            try
            {
                if (settings.ContainsKey("LogFileName")) { _log_fname = settings["LogFileName"]; }
                if (settings.ContainsKey("OrderLogFileName")) { _order_log_fname = settings["OrderLogFileName"]; }
                if (settings.ContainsKey("TickLogFileName")) { _tick_log_fname = settings["TickLogFileName"]; }
                if (settings.ContainsKey("FXClientLogFileName")) { _fxclient_log_fname = settings["FXClientLogFileName"]; }
                if (settings.ContainsKey("LogFXClientEnabled")) { _log_fxclient = bool.Parse(settings["LogFXClientEnabled"]); }
                if (settings.ContainsKey("LogTicksEnabled")) { _log_ticks = bool.Parse(settings["LogTicksEnabled"]); }
                if (settings.ContainsKey("GameServerEnabled"))    { _use_game = bool.Parse(settings["GameServerEnabled"]); }
                if (settings.ContainsKey("BoundsEnabled")) { _use_bounds = bool.Parse(settings["BoundsEnabled"]); }

                if (settings.ContainsKey("LogErrorsEnabled")) { _log_errors = bool.Parse(settings["LogErrorsEnabled"]); }
                if (settings.ContainsKey("LogTradeErrorsEnabled")) { _log_trade_errors = bool.Parse(settings["LogTradeErrorsEnabled"]); }

                if (settings.ContainsKey("LogOandaSend")) { _log_oa_out = bool.Parse(settings["LogOandaSend"]); }
                if (settings.ContainsKey("LogOandaReceive")) { _log_oa_in = bool.Parse(settings["LogOandaReceive"]); }
                if (settings.ContainsKey("LogRightEdgeSend")) { _log_re_out = bool.Parse(settings["LogRightEdgeSend"]); }
                if (settings.ContainsKey("LogRightEdgeReceive")) { _log_re_in = bool.Parse(settings["LogRightEdgeReceive"]); }

                if (settings.ContainsKey("LogUnknownEventsEnabled")) { _log_unknown_events = bool.Parse(settings["LogUnknownEventsEnabled"]); }

                if (settings.ContainsKey("LogExceptionsEnabled")) { _log_exceptions = bool.Parse(settings["LogExceptionsEnabled"]); }
                if (settings.ContainsKey("LogDebugEnabled")) { _log_debug = bool.Parse(settings["LogDebugEnabled"]); }
                if (settings.ContainsKey("ShowErrorsEnabled")) { _show_errors = bool.Parse(settings["ShowErrorsEnabled"]); }

                if (settings.ContainsKey("DataFilterType")) { _data_filter_type = (DataFilterType)Enum.Parse(typeof(DataFilterType), settings["DataFilterType"]); }
                if (settings.ContainsKey("WeekendStartDay")) { _weekend_start_day = (DayOfWeek)Enum.Parse(typeof(DayOfWeek), settings["WeekendStartDay"]); }
                if (settings.ContainsKey("WeekendEndDay")) { _weekend_end_day = (DayOfWeek)Enum.Parse(typeof(DayOfWeek), settings["WeekendEndDay"]); }
                if (settings.ContainsKey("WeekendStartTime"))     { _weekend_start_time = TimeSpan.Parse(settings["WeekendStartTime"]); }
                if (settings.ContainsKey("WeekendEndTime"))       { _weekend_end_time = TimeSpan.Parse(settings["WeekendEndTime"]); }
            }
            catch (Exception e)
            {//settings parse/load problem....
                throw new OAPluginException("Unable to load options object from RE Settings dictionary. " + e.Message, e);
            }
            return true;
        }
        public bool saveRESettings(ref RightEdge.Common.SerializableDictionary<string, string> settings)
        {
            settings["LogFileName"] = _log_fname;
            settings["OrderLogFileName"] = _order_log_fname;
            settings["TickLogFileName"] = _tick_log_fname;
            settings["FXClientLogFileName"] = _fxclient_log_fname;
            settings["GameServerEnabled"] = _use_game.ToString();
            settings["BoundsEnabled"] = _use_bounds.ToString();
            settings["LogErrorsEnabled"] = _log_errors.ToString();
            settings["LogTradeErrorsEnabled"] = _log_trade_errors.ToString();
            settings["LogTicksEnabled"] = _log_ticks.ToString();
            settings["LogFXClientEnabled"] = _log_fxclient.ToString();

            settings["LogOandaSend"] = _log_oa_out.ToString();
            settings["LogOandaReceive"] = _log_oa_in.ToString();
            settings["LogRightEdgeSend"] = _log_re_out.ToString();
            settings["LogRightEdgeReceive"] = _log_re_in.ToString();

            settings["LogUnknownEventsEnabled"] = _log_unknown_events.ToString();

            settings["LogExceptionsEnabled"] = _log_exceptions.ToString();
            settings["LogDebugEnabled"] = _log_debug.ToString();
            settings["ShowErrorsEnabled"] = _show_errors.ToString();
            settings["DataFilterType"] = _data_filter_type.ToString();
            settings["WeekendStartDay"] = _weekend_start_day.ToString();
            settings["WeekendEndDay"] = _weekend_end_day.ToString();
            settings["WeekendStartTime"] = _weekend_start_time.ToString();
            settings["WeekendEndTime"] = _weekend_end_time.ToString();
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

        private bool _use_game = true;
        [Description("Set this to true for fxGame, if false fxTrade will be used."), Category("Server")]
        public bool GameServerEnabled { set { _use_game = value; } get { return (_use_game); } }

        private bool _use_bounds = true;
        [Description("Set this to true to enable order bounds"), Category("Slippage Control")]
        public bool BoundsEnabled { set { _use_bounds = value; } get { return (_use_bounds); } }

        private double _bounds = 0.0;
        [Description("Set this to the full slip-able range size."), Category("Slippage Control")]
        public double Bounds { set { _bounds = value; } get { return (_bounds); } }

        private string _order_log_fname = "C:\\orders.xml";
        [Description("Set this to the file name for storing order information."), Category("Logging"), Editor(typeof(FilePickUITypeEditor), typeof(UITypeEditor))]
        public string OrderLogFileName { set { _order_log_fname = value; } get { return (_order_log_fname); } }

        private string _fxclient_log_fname = "C:\\fxclient.log";
        [Description("Set this to the file name for the internal fxClientAPI logging."), Category("Logging"), Editor(typeof(FilePickUITypeEditor), typeof(UITypeEditor))]
        public string FXClientLogFileName { set { _fxclient_log_fname = value; } get { return (_fxclient_log_fname); } }

        private bool _log_fxclient = false;
        [Description("Enable this for the raw internal fxClientAPI log. WARNING : this is a HUGE FILE and will contain your PASSWORD IN PLAIN TEXT!!"), Category("Logging")]
        public bool LogFXClientEnabled { set { _log_fxclient = value; } get { return (_log_fxclient); } }

        private string _tick_log_fname = "C:\\tick.log";
        [Description("Set this to the file name for logging tick data."), Category("Logging"), Editor(typeof(FilePickUITypeEditor), typeof(UITypeEditor))]
        public string TickLogFileName { set { _tick_log_fname = value; } get { return (_tick_log_fname); } }

        private bool _log_ticks = false;
        [Description("Set this to true to enable logging of tick data to the tick log."), Category("Logging")]
        public bool LogTicksEnabled { set { _log_ticks = value; } get { return (_log_ticks); } }

        private string _log_fname = "C:\\RightEdgeOandaPlugin.log";
        [Description("Set this to the file name for logging."), Category("Logging"), Editor(typeof(FilePickUITypeEditor), typeof(UITypeEditor))]
        public string LogFileName { set { _log_fname = value; } get { return (_log_fname); } }

        private bool _log_errors = true;
        [Description("Set this to true to enable logging of errors."), Category("Logging")]
        public bool LogErrorsEnabled { set { _log_errors = value; } get { return (_log_errors); } }

        private bool _log_trade_errors = true;
        [Description("Set this to true to enable logging of all order submission errors."), Category("Logging")]
        public bool LogTradeErrorsEnabled { set { _log_trade_errors = value; } get { return (_log_trade_errors); } }

        private bool _log_oa_in = true;
        [Description("Set this to true to enable logging of Oanda Account Event Responses."), Category("Event Logging")]
        public bool LogOandaReceive { set { _log_oa_in = value; } get { return (_log_oa_in); } }

        private bool _log_oa_out = true;
        [Description("Set this to true to enable logging of Oanda Account Actions."), Category("Event Logging")]
        public bool LogOandaSend { set { _log_oa_out = value; } get { return (_log_oa_out); } }

        private bool _log_re_in = true;
        [Description("Set this to true to enable logging of RightEdge broker orderbook calls."), Category("Event Logging")]
        public bool LogRightEdgeReceive { set { _log_re_in = value; } get { return (_log_re_in); } }

        private bool _log_re_out = true;
        [Description("Set this to true to enable logging of RightEdge OrderUpdated calls."), Category("Event Logging")]
        public bool LogRightEdgeSend { set { _log_re_out = value; } get { return (_log_re_out); } }

        private bool _log_exceptions = true;
        [Description("Set this to true to enable logging of exception details."), Category("Logging")]
        public bool LogExceptionsEnabled { set { _log_exceptions = value; } get { return (_log_exceptions); } }

        private bool _log_debug = true;
        [Description("Set this to true to enable debug messages."), Category("Logging")]
        public bool LogDebugEnabled { set { _log_debug = value; } get { return (_log_debug); } }
        
        private bool _show_errors = true;
        [Description("Set this to true to enable a message box dialog on errors."), Category("Logging")]
        public bool ShowErrorsEnabled { set { _show_errors = value; } get { return (_show_errors); } }

        private bool _log_unknown_events = true;
        [Description("Set this to true to enable logging of event details for unknown Oanda Account Responses."), Category("Event Logging")]
        public bool LogUnknownEventsEnabled { set { _log_unknown_events = value; } get { return (_log_unknown_events); } }

        private DataFilterType _data_filter_type = DataFilterType.WeekendTimeFrame;
        [Description("There are 3 filtering options for historic data downloads. Set this to 'WeekendTimeFrame' to enable the filter using the specified Weekend date/time range. Set it to 'PriceActivity' to filter bars with no price movement. Set it to 'None' to disable all filtering."), Category("Data Filter")]
        public DataFilterType DataFilterType { set { _data_filter_type = value; } get { return (_data_filter_type); } }

        private DayOfWeek _weekend_start_day = DayOfWeek.Friday;
        [Description("The day of the week the weekend data starts."), Category("Data Filter")]
        public DayOfWeek WeekendStartDay { set { _weekend_start_day = value; } get { return (_weekend_start_day); } }
        private TimeSpan _weekend_start_time = new TimeSpan(17, 0, 0);
        [Description("The time of day the weekend data starts."), Category("Data Filter")]
        public TimeSpan WeekendStartTime { set { _weekend_start_time = value; } get { return (_weekend_start_time); } }

        private DayOfWeek _weekend_end_day = DayOfWeek.Sunday;
        [Description("The day of the week the weekend data stops."), Category("Data Filter")]
        public DayOfWeek WeekendEndDay { set { _weekend_end_day = value; } get { return (_weekend_end_day); } }
        private TimeSpan _weekend_end_time = new TimeSpan(11, 0, 0);
        [Description("The time of day the weekend data stops."), Category("Data Filter")]
        public TimeSpan WeekendEndTime { set { _weekend_end_time = value; } get { return (_weekend_end_time); } }
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
        public double Low = 999999.99;

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
        public AccountResponder(int act_id, string base_currency, OandAPlugin p) : base() { _account_id = act_id; _base_currency = base_currency; _parent = p; }

        private OandAPlugin _parent = null;

        private bool _active = true;
        public bool Active { get { return (_active); } set { _active = value; } }

        private int _account_id = 0;
        public int AccountID { get { return (_account_id); } }

        private string _base_currency = string.Empty;
        public string BaseCurrency { get { return (_base_currency); } }

        public override void handle(fxEventInfo ei, fxEventManager em)
        {
            _parent.ResponseProcessor.HandleAccountResponder(this, (fxAccountEventInfo)ei, em);
        }
    }
    #endregion
    
    public class fxClientWrapper
    {
        public fxClientWrapper() { }

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

        private PluginLog _log = null;
        [XmlIgnore]
        public PluginLog PluginLog { set { _log = value; } get { return (_log); } }

        private string _user = string.Empty;
        private string _pw = string.Empty;

        public FXClientResult Connect(ServiceConnectOptions connectOptions, string u, string pw)
        {
            if (connectOptions == ServiceConnectOptions.Broker)
            { _log.captureDebug("Connect() called.\n--------------------"); }

            _user = u;
            _pw = pw;

            FXClientResult res = connectIn(connectOptions);
            if (res.Error) { return res; }

            if(connectOptions == ServiceConnectOptions.Broker)
            { res = connectOut(); }

            return res;
        }
        public FXClientResult Disconnect()
        {
            FXClientResult res = new FXClientResult();
            if (_fx_client_in == null) { return res; }

            try
            {
                if (_fx_client_in.IsLoggedIn)
                {
                    _fx_client_in.Logout();
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
        
        private FXClientResult connectIn(ServiceConnectOptions connectOptions)
        {
            FXClientResult res = new FXClientResult();

            if (connectOptions == ServiceConnectOptions.Broker)
            { _log.captureDebug("connectIn() called."); }

            if (_fx_client_in != null)
            {
                res.setError("Connect called on existing fxclient", FXClientResponseType.Rejected, true);
                return res;
            }

            bool wrt = false;
            bool wka = false;
            switch (connectOptions)
            {
                case ServiceConnectOptions.Broker:
                    wka = true;
                    break;
                case ServiceConnectOptions.LiveData:
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

            if (_opts.LogFXClientEnabled)
            {
                _fx_client_in.Logfile = _opts.FXClientLogFileName;
            }

            try
            {
                _fx_client_in.WithRateThread = wrt;
                _fx_client_in.WithKeepAliveThread = wka;
                _fx_client_in.Login(_user, _pw);
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

                res.setError("contact oanda when ready to turn on live",FXClientResponseType.Rejected,true);
                return res;
            }

            if (_opts.LogFXClientEnabled)
            {
                _fx_client_out.Logfile = _opts.FXClientLogFileName;
            }

            try
            {
                _fx_client_out.WithRateThread = false;
                _fx_client_out.WithKeepAliveThread = true;
                _fx_client_out.Login(_user, _pw);
                return res;
            }
            catch (SessionException oase)
            {
                _log.captureException(oase);
                res.setError("session exception : " + oase.Message,FXClientResponseType.Disconnected,true);
                return res;
            }
            catch (OAException oae)
            {
                _log.captureException(oae);
                res.setError("login failed : " + oae.Message,FXClientResponseType.Rejected,true);
                return res;
            }
            catch (Exception e)
            {
                _log.captureException(e);
                res.setError("Unhandled exception : " + e.Message,FXClientResponseType.Rejected, !_fx_client_out.IsLoggedIn);
                return res;
            }
        }
        

        public FXClientResult SetWatchedSymbols(List<RateTicker> rate_tickers, List<Symbol> symbols,OandAPlugin parent)
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
        public FXClientObjectResult<ArrayList> GetHistory(fxPair fxPair, Interval interval, int num_ticks)
        {
            FXClientObjectResult<ArrayList> res = new FXClientObjectResult<ArrayList>();
            try
            {
                if (!_fx_client_in.WithRateThread)
                {
                    res.setError("fx client has no rate table.", FXClientResponseType.Disconnected, false);
                    return res;
                }
                res.ResultObject = _fx_client_in.RateTable.GetHistory(fxPair, interval, num_ticks);
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
        
        public FXClientObjectResult<Fill> GenerateFillFromTransaction(Transaction trans, string base_currency)
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
            if (trans.Base == base_currency)
            {
                act_pr = 1.0 / sym_pr;
            }
            else if (trans.Quote != base_currency)
            {//neither the Base nor the Quote is the base_currency

                //determine a Base/base_currency and/or Quote/base_currency cross price factor
                double b_pr = OandAUtils.determineRate(_fx_client_in, trans.IsBuy(), trans.Timestamp, trans.Base, base_currency);
                double q_pr = OandAUtils.determineRate(_fx_client_in, trans.IsBuy(), trans.Timestamp, trans.Quote, base_currency);

                //FIX ME - convert act_pr using the cross factor
                throw new NotImplementedException("FIX ME - convert act_pr using the cross factor");
            }
            //else (trans.Quote == base_currency)

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
                _log.captureException(oe);
                res.setError("Oanda Order Exception : " + oe.Message, FXClientResponseType.Invalid, false);
                return res;
            }
            catch (AccountException ae)
            {
                _log.captureException(ae);
                res.setError("Oanda Account Exception : " + ae.Message, FXClientResponseType.Invalid, false);
                return res;
            }
            catch (SessionException se)
            {
                _log.captureException(se);
                res.setError("Oanda Session Exception : " + se.Message, FXClientResponseType.Disconnected, false);
                return res;
            }
            catch (OAException oae)
            {
                _log.captureException(oae);
                res.setError("General Oanda Exception : " + oae.Message, FXClientResponseType.Rejected, false);
                return res;
            }
            catch (Exception e)
            {
                _log.captureException(e);
                res.setError("Unhandled Exception : " + e.Message, FXClientResponseType.Rejected, false);
                return res;
            }
        }

        public FXClientResult AddAccountEventResponder(Account acct, AccountResponder ar)
        {
            FXClientResult res = new FXClientResult();
            try
            {
                fxEventManager em = acct.GetEventManager();

                if (!em.add(ar))
                {
                    res.setError("Unable to add account responder to the oanda event manager.", FXClientResponseType.Invalid, false);
                }
                return res;
            }
            catch (AccountException ae)
            {
                _log.captureException(ae);
                res.setError("Account Exception : " + ae.Message, FXClientResponseType.Invalid, false);
                return res;
            }
            catch (SessionException se)
            {
                _log.captureException(se);
                res.setError("Session Exception : " + se.Message, FXClientResponseType.Disconnected, false);
                return res;
            }
            catch (OAException oae)
            {
                _log.captureException(oae);
                res.setError("General Oanda Exception : " + oae.Message, FXClientResponseType.Rejected, false);
                return res;
            }
            catch (Exception e)
            {
                _log.captureException(e);
                res.setError("Unhandled Exception : " + e.Message, FXClientResponseType.Rejected, false);
                return res;
            }
        }
        
        public FXClientObjectResult<MarketOrder> GetTradeWithID(int act_id, int trade_id)
        {
            FXClientObjectResult<MarketOrder> res;
            FXClientObjectResult<AccountResult> ares = ConvertStringToAccount(act_id.ToString());
            if(ares.Error)
            {
                res = new FXClientObjectResult<MarketOrder>();
                res.setError(ares.Message,ares.FXClientResponse,ares.Disconnected);
                return res;
            }
            return GetTradeWithID(ares.ResultObject.FromOutChannel,trade_id);
        }
        public FXClientObjectResult<MarketOrder> GetTradeWithID(Account acct, int trade_id)
        {
            FXClientObjectResult<MarketOrder> res = new FXClientObjectResult<MarketOrder>();

            try
            {
                MarketOrder mo = new MarketOrder();
                if (! outChannelIsInit)
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
                _log.captureException(oe);
                res.setError("Oanda Order Exception : " + oe.Message, FXClientResponseType.Invalid, false);
                return res;
            }
            catch (AccountException ae)
            {
                _log.captureException(ae);
                res.setError("Oanda Account Exception : " + ae.Message, FXClientResponseType.Invalid, false);
                return res;
            }
            catch (SessionException se)
            {
                _log.captureException(se);
                res.setError("Oanda Session Exception : " + se.Message, FXClientResponseType.Disconnected, false);
                return res;
            }
            catch (OAException oae)
            {
                _log.captureException(oae);
                res.setError("General Oanda Exception : " + oae.Message, FXClientResponseType.Rejected, false);
                if (oae.Message == "No data available") { res.OrderMissing = true; }
                return res;
            }
            catch (Exception e)
            {
                _log.captureException(e);
                res.setError("Unhandled Exception : " + e.Message, FXClientResponseType.Rejected, false);
                return res;
            }
        }
        public FXClientObjectResult<LimitOrder> GetOrderWithID(int act_id, int id_num)
        {
            FXClientObjectResult<LimitOrder> res;
            FXClientObjectResult<AccountResult> ares = ConvertStringToAccount(act_id.ToString());
            if(ares.Error)
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
                _log.captureException(oe);
                res.setError("Oanda Order Exception : " + oe.Message, FXClientResponseType.Invalid, false);
                return res;
            }
            catch (AccountException ae)
            {
                _log.captureException(ae);
                res.setError("Oanda Account Exception : " + ae.Message, FXClientResponseType.Invalid, false);
                return res;
            }
            catch (SessionException se)
            {
                _log.captureException(se);
                res.setError("Oanda Session Exception : " + se.Message, FXClientResponseType.Disconnected, false);
                return res;
            }
            catch (OAException oae)
            {
                _log.captureException(oae);
                res.setError("General Oanda Exception : " + oae.Message, FXClientResponseType.Rejected, false);
                if (oae.Message == "No data available") { res.OrderMissing = true; }
                return res;
            }
            catch (Exception e)
            {
                _log.captureException(e);
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
                }
                _log.captureOAOut(acct, mo, "MODIFY");
                acct.Modify(mo);
                res.FXClientResponse = FXClientResponseType.Accepted;
                return res;
            }
            catch (OrderException oe)
            {
                _log.captureException(oe);
                res.setError("Oanda Order Exception : {" + oe.Code + "} " + oe.Message, FXClientResponseType.Invalid, false);
                return res;
            }
            catch (AccountException ae)
            {
                _log.captureException(ae);
                res.setError("Oanda Account Exception : {" + ae.Code + "} " + ae.Message, FXClientResponseType.Invalid, false);
                return res;
            }
            catch (SessionException oase)
            {
                _log.captureException(oase);
                res.setError("Oanda Session Exception : {" + oase.Code + "} " + oase.Message, FXClientResponseType.Disconnected, false);
                return res;
            }
            catch (OAException e)
            {
                _log.captureException(e);
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
                }
                _log.captureOAOut(acct, lo, "MODIFY");
                acct.Modify(lo);
                res.FXClientResponse = FXClientResponseType.Accepted;
                return res;
            }
            catch (OrderException oe)
            {
                _log.captureException(oe);
                res.setError("Oanda Order Exception : {" + oe.Code + "} " + oe.Message, FXClientResponseType.Invalid, false);
                return res;
            }
            catch (AccountException ae)
            {
                _log.captureException(ae);
                res.setError("Oanda Account Exception : {" + ae.Code + "} " + ae.Message, FXClientResponseType.Invalid, false);
                return res;
            }
            catch (SessionException oase)
            {
                _log.captureException(oase);
                res.setError("Oanda Session Exception : {" + oase.Code + "} " + oase.Message, FXClientResponseType.Disconnected, false);
                return res;
            }
            catch (OAException e)
            {
                _log.captureException(e);
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
                }
                _log.captureOAOut(acct, mo, "EXECUTE");
                acct.Execute(mo);
                res.FXClientResponse = FXClientResponseType.Accepted;
                return res;
            }
            catch (OrderException oe)
            {
                _log.captureException(oe);
                res.setError("Oanda Order Exception : {" + oe.Code + "} " + oe.Message, FXClientResponseType.Invalid, false);
                return res;
            }
            catch (AccountException ae)
            {
                _log.captureException(ae);
                res.setError("Oanda Account Exception : {" + ae.Code + "} " + ae.Message, FXClientResponseType.Invalid, false);
                return res;
            }
            catch (SessionException oase)
            {
                _log.captureException(oase);
                res.setError("Oanda Session Exception : {" + oase.Code + "} " + oase.Message, FXClientResponseType.Disconnected, false);
                return res;
            }
            catch (OAException e)
            {
                _log.captureException(e);
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
                }
                _log.captureOAOut(acct, lo, "EXECUTE");
                acct.Execute(lo);
                res.FXClientResponse = FXClientResponseType.Accepted;
                return res;
            }
            catch (OrderException oe)
            {
                _log.captureException(oe);
                res.setError("Oanda Order Exception : {" + oe.Code + "} " + oe.Message, FXClientResponseType.Invalid, false);
                return res;
            }
            catch (AccountException ae)
            {
                _log.captureException(ae);
                res.setError("Oanda Account Exception : {" + ae.Code + "} " + ae.Message, FXClientResponseType.Invalid, false);
                return res;
            }
            catch (SessionException oase)
            {
                _log.captureException(oase);
                res.setError("Oanda Session Exception : {" + oase.Code + "} " + oase.Message, FXClientResponseType.Disconnected, false);
                return res;
            }
            catch (OAException e)
            {
                _log.captureException(e);
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
                }
                _log.captureOAOut(acct, mo, "CLOSE");
                acct.Close(mo);
                res.FXClientResponse = FXClientResponseType.Accepted;
                return res;
            }
            catch (OrderException oe)
            {
                _log.captureException(oe);
                res.setError("Oanda Order Exception : {" + oe.Code + "} " + oe.Message, FXClientResponseType.Invalid, false);
                return res;
            }
            catch (AccountException ae)
            {
                _log.captureException(ae);
                res.setError("Oanda Account Exception : {" + ae.Code + "} " + ae.Message, FXClientResponseType.Invalid, false);
                return res;
            }
            catch (SessionException oase)
            {
                _log.captureException(oase);
                res.setError("Oanda Session Exception : {" + oase.Code + "} " + oase.Message, FXClientResponseType.Disconnected, false);
                return res;
            }
            catch (OAException e)
            {
                _log.captureException(e);
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
                }
                _log.captureOAOut(acct, lo, "CLOSE");
                acct.Close(lo);
                res.FXClientResponse = FXClientResponseType.Accepted;
                return res;
            }
            catch (OrderException oe)
            {
                _log.captureException(oe);
                res.setError("Oanda Order Exception : {" + oe.Code + "} " + oe.Message, FXClientResponseType.Invalid, false);
                return res;
            }
            catch (AccountException ae)
            {
                _log.captureException(ae);
                res.setError("Oanda Account Exception : {" + ae.Code + "} " + ae.Message, FXClientResponseType.Invalid, false);
                return res;
            }
            catch (SessionException oase)
            {
                _log.captureException(oase);
                res.setError("Oanda Session Exception : {" + oase.Code + "} " + oase.Message, FXClientResponseType.Disconnected, false);
                return res;
            }
            catch (OAException e)
            {
                _log.captureException(e);
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
                _log.captureException(oase);
                res.setError("Oanda Session Exception : {" + oase.Code + "} " + oase.Message, FXClientResponseType.Disconnected, false);
                return res;
            }
            catch (AccountException ae)
            {
                _log.captureException(ae);
                res.setError("Oanda Account Exception : {" + ae.Code + "} " + ae.Message, FXClientResponseType.Invalid, false);
                return res;
            }
            catch (OAException oae)
            {
                _log.captureException(oae);
                res.setError("Oanda General Exception : {" + oae.Code + "} " + oae.Message, FXClientResponseType.Rejected, false);
                return res;
            }
            catch (Exception e)
            {
                _log.captureException(e);
                res.setError("Unhandled exception : " + e.Message, FXClientResponseType.Rejected, false);
                return res;
            }
        }
    }

    public class ResponseProcessor
    {
        public ResponseProcessor() { }
        public ResponseProcessor(OandAPlugin parent, PluginLog log) { _log = log; _parent = parent; }

        private PluginLog _log = null;
        [XmlIgnore]
        public PluginLog PluginLog { set { _log = value; } get { return (_log); } }

        private OandAPlugin _parent = null;
        [XmlIgnore]
        public OandAPlugin Parent { set { _parent = value; } get { return (_parent); } }

        private int _transaction_retry_max = 5;
        private Dictionary<int, AccountResponder> _account_responders = new Dictionary<int, AccountResponder>();//key : account number
        private List<ResponseRecord> _response_pending_list = new List<ResponseRecord>();
        private Thread _response_processor = null;
        private bool _waiting = false;

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
            _response_processor.Join();
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

        public void HandleAccountResponder(AccountResponder ar, fxAccountEventInfo aei, fxEventManager em)
        {
            _log.captureDebug("handleAccountResponder() called.");
            Transaction trans = aei.Transaction;
            _log.captureOAIn(trans, ar.AccountID);

            ResponseRecord resp = new ResponseRecord(trans, ar.AccountID, ar.BaseCurrency);

            Monitor.Enter(_response_pending_list);
            try
            {
                _response_pending_list.Add(resp);
            }
            finally { Monitor.Pulse(_response_pending_list); Monitor.Exit(_response_pending_list); }

            if (_waiting && _response_processor.ThreadState == ThreadState.WaitSleepJoin)
            {
                _response_processor.Interrupt();
            }
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
            if (! (obj is IDString)) { return false; }
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
        public ResponseRecord(Transaction trans,int aid, string cur) { _act_id = aid; _trans = trans; _base_currency = cur; }
        public ResponseRecord(){}

        private int _act_id = 0;
        public int AccountId { set { _act_id = value; } get { return (_act_id); } }

        private int _retry_count = 0;
        public int RetryCount { set { _retry_count = value; } get { return (_retry_count); } }

        private string _base_currency = string.Empty;
        public string BaseCurrency { get { return (_base_currency); } }

        private Transaction _trans = null;
        public Transaction Transaction {set{_trans=value;}get{return (_trans);}}
    }

    [Serializable]
    public class FillRecord
    {
        public FillRecord() { }
        public FillRecord(Fill f, string n_id) { _fill = f; _id = n_id; }

        private Fill _fill = null;
        public Fill Fill { set { _fill = value; } get { return (_fill); } }

        private string _id = null;
        public string Id { set { _id = value; } get { return (_id); } }
    }

    [Serializable]
    public class OpenOrderRecord : OrderRecord
    {
        public OpenOrderRecord() { }
        public OpenOrderRecord(BrokerOrder bo, bool is_re):base(bo,is_re) { }

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
        public BrokerOrder BrokerOrder { set { _order = value; } get { return (_order); } }

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
            if (openOrder == null || openOrder.BrokerOrder==null)
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
    }

    [Serializable]
    public class TradeRecords : RightEdgeOandaPlugin.SerializableDictionary<IDString, TradeRecord>
    {//key is orderid of tr.openorder
        public TradeRecords(){ }
        public TradeRecords(IDString id){ _id = id; }

        private IDString _id;
        [XmlElement("IDValue")]
        public IDString ID { set { _id = value; } get { return (_id); } }
    }

    [Serializable]
    public class BrokerPositionRecord : BrokerPosition
    {
        public BrokerPositionRecord() { }
        public BrokerPositionRecord(string id) { _id = id; }

        private string _id;
        [XmlElement("IDValue")]
        public string ID { set { _id = value; } get { return (_id); } }

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
    }

    #region ID matching classes
    public class OrderIDRecord
    {
        public IDString OrderID = null;
        public string FillID = string.Empty;

        public OrderIDRecord() { }
        public OrderIDRecord(TradeRecord tr){InitFromTradeRecord(tr);}

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
            int n=0;
            foreach (string bpr_key in _positions.Keys)
            {
                BrokerPositionRecord bpr = _positions[bpr_key];
                foreach (IDString tr_key in bpr.TradeRecords.Keys)
                {
                    TradeRecord tr = bpr.TradeRecords[tr_key];

                    BrokerOrder trbo=tr.openOrder.BrokerOrder;
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
    [Synchronization(SynchronizationAttribute.REQUIRED)]
    public class OrderBook : ContextBoundObject
    {
        public OrderBook() { }
        public OrderBook(OAPluginOptions opts) { _opts = opts; }

        private OAPluginOptions _opts = null;
        [XmlIgnore]
        public OAPluginOptions OAPluginOptions { set { _opts = value; } get { return (_opts); } }

        private OandAPlugin _parent = null;
        [XmlIgnore]
        public OandAPlugin OAPlugin { set { _parent = value; } get { return (_parent); } }

        private PluginLog _log = null;
        [XmlIgnore]
        public PluginLog PluginLog { set { _log = value; } get { return (_log); } }

        //FIX ME - this really should be private only, but how to get it to serialize then??
        private RightEdgeOandaPlugin.SerializableDictionary<int, BrokerSymbolRecords> _accounts = new RightEdgeOandaPlugin.SerializableDictionary<int, BrokerSymbolRecords>();
        public RightEdgeOandaPlugin.SerializableDictionary<int, BrokerSymbolRecords> Accounts { set { _accounts = value; } get { return (_accounts); } }

        private RightEdgeOandaPlugin.SerializableDictionary<string, List<FillRecord>> _fill_queue = new RightEdgeOandaPlugin.SerializableDictionary<string, List<FillRecord>>();

        private void addFillrecord(string pair, Fill fill, int id)
        {
            if (!_fill_queue.ContainsKey(pair))
            {
                _fill_queue[pair] = new List<FillRecord>();
            }
            _fill_queue[pair].Add(new FillRecord(fill, id.ToString()));
        }
        
        public void LogOrderBook(string s)
        {
            /*
            log.captureDebug(s);
            log.captureDebug("  orderbook has " + _accounts.Count + " accounts");
            foreach (int act_id in _accounts.Keys)
            {
                BrokerSymbolRecords bsr = _accounts[act_id];
                log.captureDebug("  account '" + act_id + "' has " + bsr.Count + " symbol positions");

                foreach (string pos_key in bsr.Keys)
                {
                    BrokerPositionRecords tbprl = bsr[pos_key];
                    log.captureDebug("    position list[" + pos_key + "] has " + tbprl.RecordCount + " positions");
                    foreach (string bprl_key in tbprl.Keys)
                    {
                        BrokerPositionRecord tbpr = tbprl[bprl_key];
                        log.captureDebug("      position record[" + bprl_key + "] has " + tbpr.RecordCount + " trades");
                    }
                }
            }
             * */
            return;
        }

        private TransactionFetchResult fetchBrokerPositionRecordByTransResponse(ResponseRecord response)
        {
            TransactionFetchResult fetch_ret = new TransactionFetchResult();

            //find the bprl for response acct/sym
            int act_id = response.AccountId;
            string sym_id = response.Transaction.Base + "/" + response.Transaction.Quote;

            if (!_accounts.ContainsKey(act_id))
            {
                fetch_ret.setError("unable to locate orderbook symbol page for account id '" + act_id.ToString() + "'");
                return fetch_ret;
            }

            BrokerSymbolRecords bsrl = _accounts[act_id];

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
            foreach (int account_id in _accounts.Keys)
            {
                BrokerSymbolRecords bsrl = _accounts[account_id];
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
            foreach (int account_id in _accounts.Keys)
            {
                BrokerSymbolRecords bsrl = _accounts[account_id];
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



        private PositionFetchResult fetchBrokerPositionRecord(int act_id, string sym_id, string pos_id)
        {
            PositionFetchResult ret = new PositionFetchResult();
            if (!_accounts.ContainsKey(act_id))
            {
                ret.setError("account record not found for account id '" + act_id + "'");
                return ret;
            }

            BrokerSymbolRecords bsrl = _accounts[act_id];

            if (!bsrl.ContainsKey(sym_id))
            {
                ret.setError("symbol record not found for symbol id '" + sym_id + "'");
                return ret;
            }

            BrokerPositionRecords bprl = bsrl[sym_id];

            if (!bprl.PositionExists(pos_id))
            {
                ret.setError("position record not found for position id '" + pos_id + "'");
                return ret;
            }

            ret = bprl.FetchPosition(pos_id);
            ret.AccountId = act_id;
            return ret;
        }


        private TransactionFetchResult fetchBrokerPositionRecordByBestFit(int act_id, BrokerOrder order)
        {//this order is not able to be found by pos id...
            TransactionFetchResult ret = new TransactionFetchResult();

            if (order.TransactionType != TransactionType.Sell && order.TransactionType != TransactionType.Cover)
            {
                ret.setError("Only close requests can be matched by best fit.");
                return ret;
            }
            
            if (!_accounts.ContainsKey(act_id))
            {
                ret.setError("account record not found for account id '" + act_id + "'");
                return ret;
            }

            BrokerSymbolRecords bsrl = _accounts[act_id];
            string sym_id = order.OrderSymbol.Name;

            if (!bsrl.ContainsKey(sym_id))
            {
                ret.setError("symbol record not found for symbol id '" + sym_id + "'");
                return ret;
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
            ret.setError("No open order found which fits this close request.");
            return ret;
        }



        private FunctionResult pushTradeRecord(int act_id, BrokerOrder open_order, bool is_re)
        {
            FunctionResult res = new FunctionResult();
            BrokerPositionRecords bprl;
            PositionFetchResult fetch_bpr = new PositionFetchResult();

            fetch_bpr.AccountId = act_id;
            fetch_bpr.SymbolName = open_order.OrderSymbol.Name;

            if (!_accounts.ContainsKey(act_id))
            {
                _accounts[act_id] = new BrokerSymbolRecords(act_id);
            }

            BrokerSymbolRecords bsrl = _accounts[act_id];

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

                bprl.Positions.Add(bpr.ID,bpr);

                fetch_bpr.ResultObject = bpr;
            }

            return fetch_bpr.ResultObject.pushTrade(open_order, is_re);
        }


        #region order submission
        public FXClientResult SubmitLimitOrder(BrokerOrder order, Account acct)
        {
            FXClientResult res;

            fxPair oa_pair = new fxPair(order.OrderSymbol.ToString());
            LimitOrder lo = new LimitOrder();

            lo.Base = oa_pair.Base;
            lo.Quote = oa_pair.Quote;

            lo.Units = (int)order.Shares;
            if (order.TransactionType == TransactionType.Short)
            { lo.Units = -1 * lo.Units; }

            //FIX ME<-- extract an order specific bounds value from the order/symbol tags...
            double slippage = _opts.Bounds;//use the broker value as the fallback

            if (_opts.BoundsEnabled)//always honor the broker enabled setting
            {
                lo.HighPriceLimit = order.LimitPrice + 0.5 * slippage;
                lo.LowPriceLimit = order.LimitPrice - 0.5 * slippage;
            }
            lo.Price = order.LimitPrice;

            int h = 36;//FIX ME - how many hours should a limit order last???
            if (h != 0)
            {
                DateTime duration = new DateTime(DateTime.UtcNow.Ticks);
                duration = duration.AddHours(h);
                lo.Duration = duration;
            }
            //else - the default limit order duration is 1 hour

            res = _parent.fxClient.SendOAExecute(acct, lo);
            if (res.Error) { return res; }

            order.OrderState = BrokerOrderState.Submitted;
            order.OrderId = lo.Id.ToString();

            FunctionResult fres = pushTradeRecord(acct.AccountId, order, true);
            if (fres.Error) { res.setError(fres.Message); }
            return res;
        }
        public FXClientResult SubmitMarketOrder(BrokerOrder order, Account acct)
        {
            FXClientResult res;
            fxPair oa_pair = new fxPair(order.OrderSymbol.ToString());
            MarketOrder mo = new MarketOrder();

            mo.Base = oa_pair.Base;
            mo.Quote = oa_pair.Quote;

            mo.Units = (int)order.Shares;
            if (order.TransactionType == TransactionType.Short)
            { mo.Units = -1 * mo.Units; }

            if (_opts.BoundsEnabled)//always honor the broker enabled setting
            {
                //FIX ME<-- extract an order specific bounds value from the order/symbol tags...
                double slippage = _opts.Bounds;//use the broker value as the fallback
                double price = order.LimitPrice;
                if (price != 0.0)
                {
                    mo.HighPriceLimit = price + 0.5 * slippage;
                    mo.LowPriceLimit = price - 0.5 * slippage;
                }
            }

            res = _parent.fxClient.SendOAExecute(acct, mo);
            if (res.Error) { return res; }

            order.OrderState = BrokerOrderState.Submitted;
            order.OrderId = mo.Id.ToString();

            FunctionResult fres = pushTradeRecord(acct.AccountId, order, true);
            if (fres.Error) { res.setError(fres.Message); }
            return res;
        }

        public FXClientResult SubmitCloseOrder(BrokerOrder order, Account acct)
        {

            FXClientResult res = new FXClientResult();
            PositionFetchResult fetch_bpr = null;
            int orders_sent = 0;
            BrokerPositionRecord cp = null;
            try
            {
                fetch_bpr = fetchBrokerPositionRecord(acct.AccountId, order.OrderSymbol.ToString(), order.PositionID);
                if (fetch_bpr.Error)
                {
                    //since their is no orderID in the order and PositionID is not valid
                    //have to do "best fit" matching.... ughhh...
                    TransactionFetchResult tfetch_bpr = fetchBrokerPositionRecordByBestFit(acct.AccountId, order);
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
                    FunctionResult fr = pushTradeRecord(acct.AccountId,tr.openOrder.BrokerOrder,true);
                    if (fr.Error)
                    {
                        res.setError("Unable to push new position for cancel to close. " + fr.Message, FXClientResponseType.Rejected, false);
                        return res;
                    }

                    //now fetch the new position and
                    fetch_bpr = fetchBrokerPositionRecord(acct.AccountId, order.OrderSymbol.ToString(), order.PositionID);
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
                            if (!subres.OrderMissing) { return subres; }
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

                    tr.closeOrder = new OrderRecord(bro,false);

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
                res.setError("Unhandled exception while closing orders. : " + e.Message,FXClientResponseType.Rejected,false);
                if (cp!=null && orders_sent == 0) { cp.CloseOrder = null; }
                return res;
            }
        }


        public FXClientResult SubmitPositionStopLossOrder(BrokerOrder order, Account acct)
        {
            FXClientResult res;
            PositionFetchResult fetch_bpr = null;
            try
            {
                fetch_bpr = fetchBrokerPositionRecord(acct.AccountId, order.OrderSymbol.ToString(), order.PositionID);
                if (fetch_bpr.Error)
                {
                    res = new FXClientResult();
                    res.setError("Unable to locate stop order's position record : '" + fetch_bpr.Message + "'.",FXClientResponseType.Rejected,false);
                    return res;
                }

                BrokerPositionRecord bpr = fetch_bpr.ResultObject;

                IDString n_id = new IDString(IDType.Stop, int.Parse(order.PositionID), bpr.StopNumber++);
                order.OrderId = n_id.ID;

                return SubmitStopOrders(bpr, order, acct);
            }
            catch (Exception e)
            {
                res = new FXClientResult();
                res.setError("Unhandled exception : '" + e.Message + "'.",FXClientResponseType.Rejected,false);
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
                bpr.StopOrder = new OrderRecord(order,true);
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

                        //gett a fresh account object (if the output channel was lost, the previous one will be invalid)
                        FXClientObjectResult<AccountResult> acres = _parent.fxClient.ConvertStringToAccount(act_id.ToString());
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
                        FXClientObjectResult<AccountResult> acres = _parent.fxClient.ConvertStringToAccount(act_id.ToString());
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

                    res.setError("Unknown open order state for stop modification. {id='" + tr.openOrder.BrokerOrder.OrderId + "' posid='" + tr.openOrder.BrokerOrder.PositionID + "' type='" + tr.openOrder.BrokerOrder.OrderType + "' state='" + tr.openOrder.BrokerOrder.OrderState + "'}",FXClientResponseType.Rejected,false);
                    res.OrdersSent = orders_sent;
                    return res;
                }
            }
            #endregion

            res.FXClientResponse = FXClientResponseType.Accepted;
            res.OrdersSent = orders_sent;
            return res;
        }

        public FXClientResult SubmitPositionTargetProfitOrder(BrokerOrder order, Account acct)
        {
            FXClientResult res;
            PositionFetchResult fetch_bpr = null;
            try
            {
                fetch_bpr = fetchBrokerPositionRecord(acct.AccountId, order.OrderSymbol.ToString(), order.PositionID);
                if (fetch_bpr.Error)
                {
                    res = new FXClientResult();
                    res.setError("Unable to locate target order's position record : '" + fetch_bpr.Message + "'.",FXClientResponseType.Rejected,false);
                    return res;
                }

                BrokerPositionRecord bpr = fetch_bpr.ResultObject;

                IDString n_id = new IDString(IDType.Target, int.Parse(order.PositionID), bpr.TargetNumber++);
                order.OrderId = n_id.ID;

                return SubmitTargetOrders(bpr, order, acct);
            }
            catch (Exception e)
            {
                res = new FXClientResult();
                res.setError("Unhandled exception : '" + e.Message + "'.",FXClientResponseType.Rejected,false);
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
                res.setError("ptarget order already exists!",FXClientResponseType.Rejected,false);
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
                            res.setError(fores.Message,fores.FXClientResponse,fores.Disconnected);
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
                        FXClientObjectResult<AccountResult> acres = _parent.fxClient.ConvertStringToAccount(act_id.ToString());
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
                        FXClientObjectResult<AccountResult> acres = _parent.fxClient.ConvertStringToAccount(act_id.ToString());
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

                    res.setError("Unknown open order state for target modification. {id='" + tr.openOrder.BrokerOrder.OrderId + "' posid='" + tr.openOrder.BrokerOrder.PositionID + "' type='" + tr.openOrder.BrokerOrder.OrderType + "' state='" + tr.openOrder.BrokerOrder.OrderState + "'}",FXClientResponseType.Rejected,false);
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
                res.setError(fetch_bpr.Message,FXClientResponseType.Rejected,false);
                return res;
            }

            BrokerPositionRecord bpr = fetch_bpr.ResultObject;

            int orders_to_send;
            FXClientObjectResult<AccountResult> ares;
            BrokerOrder bro = null;
            FXClientTaskResult tres = null;
            Account acct;
            BrokerOrderState orig_state;

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


                    ares = _parent.fxClient.ConvertStringToAccount(bro.OrderSymbol.Exchange);
                    if (ares.Error)
                    {
                        res.setError(ares.Message, ares.FXClientResponse, ares.Disconnected);
                        return res;
                    }

                    acct = ares.ResultObject.FromOutChannel;

                    tres = _parent.ResponseProcessor.ActivateAccountResponder(acct.AccountId);
                    if (tres.Error)
                    {
                        res.setError(tres.Message, tres.FXClientResponse, tres.Disconnected);
                        return res;
                    }

                    ////////////////////
                    orders_to_send = 0;//FIX ME

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
                            if (tres.TaskCompleted) { _parent.ResponseProcessor.DeactivateAccountResponder(acct.AccountId); }
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
                        if (tres.TaskCompleted) { _parent.ResponseProcessor.DeactivateAccountResponder(acct.AccountId); }

                        FunctionResult fr=ClearAllFinalizedPositions();
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

                    ares = _parent.fxClient.ConvertStringToAccount(bro.OrderSymbol.Exchange);
                    if (ares.Error)
                    {
                        res.setError(ares.Message, ares.FXClientResponse, ares.Disconnected);
                        return res;
                    }

                    acct = ares.ResultObject.FromOutChannel;

                    tres = _parent.ResponseProcessor.ActivateAccountResponder(acct.AccountId);
                    if (tres.Error)
                    {
                        res.setError(tres.Message, tres.FXClientResponse, tres.Disconnected);
                        return res;
                    }
                    
                    

                    ////////////////////
                    orders_to_send = 0;//FIX ME

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
                            if (tres.TaskCompleted) { _parent.ResponseProcessor.DeactivateAccountResponder(acct.AccountId); }
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
                        if (tres.TaskCompleted) { _parent.ResponseProcessor.DeactivateAccountResponder(acct.AccountId); }

                        FunctionResult fr=ClearAllFinalizedPositions();
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
                    res.setError("Unable to process order id prefix order ID '" + oid.ID + "'.",FXClientResponseType.Rejected,false);
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

            if (! bpr.TradeRecords.ContainsKey(oid) )
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
            FXClientObjectResult<AccountResult> ares = _parent.fxClient.ConvertStringToAccount(bro.OrderSymbol.Exchange);
            if (ares.Error)
            {
                res.setError(ares.Message, ares.FXClientResponse, ares.Disconnected);
                return res;
            }

            FXClientTaskResult tres = _parent.ResponseProcessor.ActivateAccountResponder(ares.ResultObject.FromOutChannel.AccountId);
            if (tres.Error)
            {
                res.setError(tres.Message, tres.FXClientResponse, tres.Disconnected);
                return res;
            }

            FXClientObjectResult<LimitOrder> lores = _parent.fxClient.GetOrderWithID(ares.ResultObject.FromOutChannel, oid.Num);
            if (lores.Error)
            {
                res.setError("Unable to locate the corresponding oanda broker order for id '" + oid.ID + "'.", lores.FXClientResponse, lores.Disconnected);
                if (tres.TaskCompleted) { _parent.ResponseProcessor.DeactivateAccountResponder(ares.ResultObject.FromOutChannel.AccountId); }
                return res;
            }

            return _parent.fxClient.SendOAClose(ares.ResultObject.FromOutChannel, lores.ResultObject);
            #endregion
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
                            result.setError(fillres.Message,fillres.FXClientResponse,fillres.Disconnected);
                            return result;
                        }
                        addFillrecord(trans.Base + "/" + trans.Quote, fillres.ResultObject, trans.TransactionNumber);
                        return result;//wait for the "Order Fulfilled" event to finalize the original limit order
                    }

                    _log.captureDebug("  TRANSACTION ISSUE : '" + fetch_bpr.Message + "'");
                    _parent.RESendNoMatch(response);
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
                    #region open order linked id match
                    if (desc == "Order Fulfilled")
                    {//this transaction is a notice that a limit order has been filled
                        #region limit order filled
                        //make sure this openOrder is an unfilled limit
                        if (tr.openOrder.BrokerOrder.OrderType != OrderType.Limit)
                        {//what the heck?!?
                            result.setError("Order fullfilment event received on non-limit order id '" + tr.openOrder.BrokerOrder.OrderId + "' type '" + tr.openOrder.BrokerOrder.OrderType + "'.");
                            return result;
                        }

                        if (tr.openOrder.BrokerOrder.OrderState != BrokerOrderState.Submitted)
                        {//the order was filled at oanda and is in a bad way in RE....
                            result.setError("TBI - this should be more thorough...not all states are unusable, and those that are need an Update()");
                            return result;
                        }

                        if (!_fill_queue.ContainsKey(fetch_bpr.SymbolName))
                        {//no queued up fills for this symbol!!
                            result.setError("No fill record in the queue for symbol '" + fetch_bpr.SymbolName + "'.");
                            return result;
                        }

                        //do a lookup on the queue of fill records for a fill that matches (size, etc..)this openOrder
                        foreach (FillRecord fr in _fill_queue[fetch_bpr.SymbolName])
                        {
                            if (fr.Fill.Quantity == tr.openOrder.BrokerOrder.Shares)
                            {
                                tr.openOrder.FillId = fr.Id;

                                //remove the fill record from the queue
                                _fill_queue[fetch_bpr.SymbolName].Remove(fr);
                                if (_fill_queue[fetch_bpr.SymbolName].Count == 0) { _fill_queue.Remove(fetch_bpr.SymbolName); }

                                //update the openOrder as filled
                                _parent.RESendFilledOrder(fr.Fill, tr.openOrder.BrokerOrder, "limit order fullfilment");
                                return result;
                            }
                        }
                        result.setError("No fill record for the order id '" + tr.openOrder.BrokerOrder.OrderId + "' symbol '" + fetch_bpr.SymbolName + "'.");
                        return result;
                        #endregion
                    }
                    else if (desc == "Cancel Order")
                    {
                        #region cancel order
                        tr.openOrder.BrokerOrder.OrderState = BrokerOrderState.Cancelled;
                        _parent.FireOrderUpdated(tr.openOrder.BrokerOrder, null, "order canceled");

                        FXClientResult fres=finalizePositionExit(pos,"cancel order");
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
                        _log.captureDebug("  handleAccountTransaction() - preparing to modify a trade...");

                        double sl = trans.Stop_loss;
                        double tp = trans.Take_profit;

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
                            bool isre=tr.closeOrder.IsRightEdgeOrder;
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
                            BrokerOrder cbo=tr.closeOrder.BrokerOrder;
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

            List<int> act_keys = new List<int>(_accounts.Keys);
            foreach (int act_id in act_keys)
            {
                if (!_accounts.ContainsKey(act_id)) { continue; }
                BrokerSymbolRecords bsrl = _accounts[act_id];
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
                                if(tr.closeOrder != null)
                                {
                                    safe_to_remove = false;
                                    continue;
                                }
                                if ( tr.openOrder != null && !tr.openOrder.StopHit && !tr.openOrder.TargetHit )
                                {
                                    safe_to_remove = false;
                                    continue;
                                }
                            }
                            if (safe_to_remove)
                            {
                                //pull out bpr, remove any now empty orderbook pages too
                                if (!bprl.Positions.Remove(pos_id))
                                {
                                    res.setError("Unable to remove position record '" + pos_id + "'");
                                    return res;
                                }
                                if (bprl.Positions.Count == 0)
                                {
                                    if (!bsrl.Remove(sym_id))
                                    {
                                        res.setError("Unable to remove symbol record '" + sym_id + "'");
                                        return res;
                                    }
                                }
                                if (bsrl.Count == 0)
                                {
                                    if (!_accounts.Remove(act_id))
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
            return res;
        }
        #endregion


        #region XML Serialization
        private string _fname = "";
        [XmlIgnore, Browsable(false)]
        public string OrderLogFileName { set { _fname = value; } get { return (_fname); } }

        public void saveSettings()
        {
            XmlSerializer mySerializer = new XmlSerializer(typeof(OrderBook));
            StreamWriter myWriter = new StreamWriter(_fname);
            mySerializer.Serialize(myWriter, this);
            myWriter.Close();
            myWriter.Dispose();
        }

        public static OrderBook loadSettings(string opt_fname)
        {
            XmlSerializer mySerializer = new XmlSerializer(typeof(OrderBook));
            OrderBook ob;
            FileStream myFileStream = null;
            try
            {
                myFileStream = new FileStream(opt_fname, FileMode.Open);
            }
            catch (System.IO.IOException)
            {
                ob = new OrderBook();
                ob.OrderLogFileName = opt_fname;
                return (ob);
            }
            ob = (OrderBook)mySerializer.Deserialize(myFileStream);
            myFileStream.Close();
            myFileStream.Dispose();
            ob.OrderLogFileName = opt_fname;
            return (ob);
        }
        #endregion

        public List<BrokerOrder> GetOpenBrokerOrderList()
        {
            List<BrokerOrder> list = new List<BrokerOrder>();
            foreach (int act_id in _accounts.Keys)
            {
                BrokerSymbolRecords bsr = _accounts[act_id];

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

            foreach (int act_id in _accounts.Keys)
            {
                BrokerSymbolRecords bsr = _accounts[act_id];

                foreach (string sym_key in bsr.Keys)
                {
                    BrokerPositionRecords bprl = bsr[sym_key];
                    foreach (string bpr_key in bprl.Positions.Keys)
                    {
                        BrokerPositionRecord bpr = bprl.Positions[bpr_key];
                        if (bpr.StopOrder != null && bpr.StopOrder.BrokerOrder.OrderId == id)
                        { return (bpr.StopOrder.BrokerOrder); }
                        else if (bpr.TargetOrder != null && bpr.TargetOrder.BrokerOrder.OrderId == id)
                        { return (bpr.TargetOrder.BrokerOrder); }

                        foreach (IDString tr_key in bpr.TradeRecords.Keys)
                        {
                            TradeRecord tr = bpr.TradeRecords[tr_key];
                            if (tr.openOrder.BrokerOrder.OrderId == id)
                            { return (tr.openOrder.BrokerOrder); }
                        }//end of trade record loop
                    }//end of position loop
                }//end of symbol loop
            }//end of account loop
            return null;
        }

        public int GetSymbolShares(string symbol_name)
        {
            //FIX ME <-- look to a property for the account number here
            foreach (int act_id in _accounts.Keys)
            {//for now just use the first one returned in the hash keys...there's probably only one anyway...
                BrokerSymbolRecords bsr = _accounts[act_id];

                if (!bsr.ContainsKey(symbol_name)) { return (0); }
                BrokerPositionRecords bprl = bsr[symbol_name];

                return bprl.getTotalSize();
            }
            return 0;
        }

        public FXClientObjectResult<double> GetMarginAvailable()
        {
            //FIX ME <-- look to a property for the account number here
            FXClientObjectResult<AccountResult> ares = _parent.fxClient.ConvertStringToAccount("");
            if (ares.Error)
            {
                FXClientObjectResult<double> res = new FXClientObjectResult<double>();
                res.setError(ares.Message, ares.FXClientResponse, ares.Disconnected);
                return res;
            }

            return _parent.fxClient.GetMarginAvailable(ares.ResultObject.FromOutChannel);
        }

        public FXClientResult CancelAllOrders()
        {
            FXClientResult res = new FXClientResult();

            foreach (int act_id in _accounts.Keys)
            {
                BrokerSymbolRecords bsr = _accounts[act_id];
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
    }
    

    public class OandAPlugin : IService, IBarDataRetrieval, ITickRetrieval, IBroker
    {
        public OandAPlugin()
        {
            //System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.TextWriterTraceListener("c:\\Storage\\src\\trace.log"));
            //System.Diagnostics.Trace.AutoFlush = true;
        }
        ~OandAPlugin() { }

        private int _main_thread_id;

        private int _fail_ticket_num = 1;

        private fxClientWrapper _fx_client = new fxClientWrapper();
        public fxClientWrapper fxClient { get { return (_fx_client); } }

        private ResponseProcessor _response_processor = null;
        public ResponseProcessor ResponseProcessor { get { return (_response_processor); } }

        private OAPluginOptions _opts = null;
        private ServiceConnectOptions _connected_as = ServiceConnectOptions.None;

        private List<int> _reconnect_account_ids = new List<int>();
        
        private OrderBook _orderbook = new OrderBook();
        public OrderBook OrderBook { get { return (_orderbook); } }

        private static PluginLog _log = new PluginLog();
        private static PluginLog _tick_log = new PluginLog();

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
            OAPluginOptions topts= new OAPluginOptions(_opts);
            topts.loadRESettings(settings);
            
            OandAPluginOptionsForm frm = new OandAPluginOptionsForm(topts);
            DialogResult res = frm.ShowDialog();
            if (res == DialogResult.Cancel)
            { return false; }
            
            _opts = frm.Opts;
            return _opts.saveRESettings(ref settings);
        }

        public bool Initialize(RightEdge.Common.SerializableDictionary<string, string> settings)
        {
            if (_opts == null) { _opts = new OAPluginOptions(); }
            bool r=_opts.loadRESettings(settings);
            _log.FileName = _opts.LogFileName;
            _log.LogErrors = _opts.LogErrorsEnabled;
            _log.ShowErrors = _opts.ShowErrorsEnabled;
            _log.LogDebug = _opts.LogDebugEnabled;
            _log.LogExceptions = _opts.LogExceptionsEnabled;

            _log.LogReceiveOA = _opts.LogOandaReceive;
            _log.LogReceiveRE =_opts.LogRightEdgeReceive;
            _log.LogSendOA = _opts.LogOandaSend;
            _log.LogSendRE = _opts.LogRightEdgeSend;

            _log.LogUnknownEvents = _opts.LogUnknownEventsEnabled;

            _tick_log.FileName = _opts.TickLogFileName;
            _tick_log.LogDebug = _opts.LogTicksEnabled;

            _orderbook.OrderLogFileName = _opts.OrderLogFileName;

            _orderbook.OAPluginOptions = _opts;
            return r;
        }

        
        // Implements connection to a service functionality.
        // RightEdge will call this function before requesting
        // service data.  Return true if the connection is
        // successful, otherwise, false.
        public bool Connect(ServiceConnectOptions connectOptions)
        {
            clearError();

            _main_thread_id = Thread.CurrentThread.ManagedThreadId;

            if (_fx_client.OAPluginOptions == null) { _fx_client.OAPluginOptions = _opts; }
            if (_fx_client.PluginLog == null) { _fx_client.PluginLog = _log; }

            if (_orderbook.OAPlugin == null) { _orderbook.OAPlugin = this; }
            if (_orderbook.PluginLog == null) { _orderbook.PluginLog = _log; }

            FXClientResult cres = _fx_client.Connect(connectOptions,_username,_password);
            if (cres.Error)
            {
                _fx_client.Disconnect();
                _log.captureError(cres.Message, "Connect Error");
                return false;
            }

            if (connectOptions == ServiceConnectOptions.Broker)
            {
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
                        _log.captureDebug("Connect re-establishing account listener for account '" + aid + "'.");

                        TaskResult tres = _response_processor.ActivateAccountResponder(aid);
                        if (tres.Error)
                        {
                            _fx_client.Disconnect();
                            _log.captureError(tres.Message, "Connect Error");

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

                _orderbook.LogOrderBook("disconnecting");

                if(!string.IsNullOrEmpty(_orderbook.OrderLogFileName))
                {
                    _orderbook.saveSettings();
                }
            }

            FXClientResult res = _fx_client.Disconnect();
            if (res.Error)
            {
                _log.captureError(res.Message,"Disconnect Error");
                return false;
            }
            return true;
        }

        private string _error_str=string.Empty;
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
                    _log.captureError(_error_str,  "RetrieveData Error");
                    return null;
                }

                List<BarData> list = new List<BarData>();
                FXClientObjectResult<ArrayList> hal =  _fx_client.GetHistory(new fxPair(symbol.Name), interval, num_ticks);
                if (hal.Error)
                {
                    if (hal.Disconnected)
                    {
                        _error_str = "Disconnected : " + hal.Message;
                        _log.captureError(_error_str, "RetrieveData Error");
                        _fx_client.Disconnect();
                        return null;
                    }
                    _error_str = hal.Message;
                    _log.captureError(_error_str, "RetrieveData Error");
                    return null;
                }

                DataFilterType filter_type = _opts.DataFilterType;
                DayOfWeek weekend_start_day = _opts.WeekendStartDay;
                DayOfWeek weekend_end_day = _opts.WeekendEndDay;
                TimeSpan weekend_start_time = _opts.WeekendStartTime;
                TimeSpan weekend_end_time = _opts.WeekendEndTime;
                bool drop_bar = true;

                System.Collections.IEnumerator iEnum = hal.ResultObject.GetEnumerator();
                while (iEnum.MoveNext())
                {
                    fxHistoryPoint hp = (fxHistoryPoint)iEnum.Current;
                    DateTime hpts = hp.Timestamp;

                    if (hpts < startDate) { continue; }

                    drop_bar = true;
                    switch (filter_type)
                    {
                        case DataFilterType.WeekendTimeFrame:
                            if (hpts.DayOfWeek >= weekend_start_day || hpts.DayOfWeek <= weekend_end_day)
                            {
                                if (hpts.DayOfWeek == weekend_start_day && hpts.TimeOfDay < weekend_start_time)
                                { drop_bar = false; }

                                if (hpts.DayOfWeek == weekend_end_day && hpts.TimeOfDay >= weekend_end_time)
                                { drop_bar = false; }
                            }
                            break;
                        case DataFilterType.PriceActivity:
                            CandlePoint cp = hp.GetCandlePoint();
                            double n=cp.Open;
                            if(n==cp.Close && n==cp.Min && n==cp.Max)
                            { drop_bar = false; }
                            break;
                        case DataFilterType.None:
                            drop_bar = false;
                            break;
                        default:
                            _error_str = "Unknown Data Filter Type setting '" + filter_type + "'.";
                            _log.captureError(_error_str, "RetrieveData Error");
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
                _log.captureException(e);
                _error_str = "Unhandled exception : " + e.Message;
                _log.captureError(_error_str,  "RetrieveData Error");
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
            _tick_log.captureDebug("TICK : time='" + tick.time + "' price='" + tick.price + "' type='" + tick.tickType + "' size='" + tick.size + "'");
            _gtd_event(sym, tick);
        }

        private List<RateTicker> _rate_tickers= new List<RateTicker>();

        public void handleRateTicker(RateTicker rt,fxRateEventInfo ei, fxEventManager em)
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
                _log.captureException(e);
                _error_str = "Unhandled exception : " + e.Message;
                _log.captureError(_error_str,  "RetrieveData Error");
                throw new OAPluginException(_error_str, e);
            }

        }

        // This is called by RightEdge to set the symbol list
        // that is requested by the user.
        public bool SetWatchedSymbols(List<Symbol> symbols)
        {
            FXClientResult res = _fx_client.SetWatchedSymbols(_rate_tickers, symbols,this);
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
                _log.captureError(_error_str,  "SetWatchedSymbols Error");
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
            if (string.IsNullOrEmpty(_orderbook.OrderLogFileName))
            {//the plugin doesn't know anything about what might be in accountState
                //if there is anything in the accontState there's no way to reconnect to the transaction at oanda
                //so throw an error
                return;
            }

            OrderBook saved_orderbook = OrderBook.loadSettings(_orderbook.OrderLogFileName);

            //merge the info in accountState and saved_orderbook into the active _orderbook
            //and load the _handling_account_ids list with account id numbers of the relevant accounts
        }
        #endregion



        #region orderbok operations
        private bool submitOrderHandleResult(BrokerOrder order,FXClientResult r,int act_id,bool added, out string orderId)
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

                if (added && r.OrdersSent==0) { _response_processor.DeactivateAccountResponder(act_id); }
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

            if (! _fx_client.IsInit)//the fx_client is gone, there was a disconnection event...
            {//sometimes RE acts again before checking the error from the last action
                rejectOrder(order, out orderId);
                _error_str = "Disconnected : Broker not connected!";
                _log.captureError(_error_str, "SubmitOrder Error");
                return false;//this should re-trigger the disconnect logic in RE
            }

            Account acct = null;
            FXClientResult r;
            FXClientTaskResult tres = null;
            try
            {
                FXClientObjectResult<AccountResult> ares = _fx_client.ConvertStringToAccount(order.OrderSymbol.Exchange);
                if(ares.Error)
                {
                    setResponseErrorMessage(ares);
                    _log.captureError(_error_str, "SubmitOrder Error");
                    returnOrder(ares.FXClientResponse,order, out orderId);
                    return false;
                }

                acct = ares.ResultObject.FromOutChannel;

                tres = _response_processor.ActivateAccountResponder(acct.AccountId);
                if (tres.Error)
                {
                    setResponseErrorMessage(tres); 
                    _log.captureError(_error_str, "SubmitOrder Error");
                    returnOrder(tres.FXClientResponse,order, out orderId);
                    return false;
                }

                TransactionType ott = order.TransactionType;
                switch (order.OrderType)
                {
                    case OrderType.Market:
                        if (ott == TransactionType.Sell || ott == TransactionType.Cover)
                        {
                            r = _orderbook.SubmitCloseOrder(order, acct);
                            return submitOrderHandleResult(order, r, acct.AccountId, tres.TaskCompleted, out orderId);
                        }
                        else if (ott == TransactionType.Buy || ott == TransactionType.Short)
                        {
                            r = _orderbook.SubmitMarketOrder(order, acct);
                            return submitOrderHandleResult(order, r, acct.AccountId, tres.TaskCompleted, out orderId);
                        }
                        else
                        {
                            rejectOrder(order, out orderId);
                            _error_str = "Unknown market order transaction type '" + ott + "'.";
                            _log.captureError(_error_str, "SubmitOrder Error");
                            if (tres.TaskCompleted) { _response_processor.DeactivateAccountResponder(acct.AccountId); }
                            return false;
                        }
                    case OrderType.Limit:
                        if (order.TransactionType == TransactionType.Sell || order.TransactionType == TransactionType.Cover)
                        {
                            r = _orderbook.SubmitPositionTargetProfitOrder(order, acct);
                            return submitOrderHandleResult(order, r, acct.AccountId, tres.TaskCompleted, out orderId);
                        }
                        else if (ott == TransactionType.Buy || ott == TransactionType.Short)
                        {
                            r = _orderbook.SubmitLimitOrder(order, acct);
                            return submitOrderHandleResult(order, r, acct.AccountId, tres.TaskCompleted, out orderId);
                        }
                        else
                        {
                            rejectOrder(order, out orderId);
                            _error_str =  "Unknown limit order transaction type '" + ott + "'.";
                            _log.captureError(_error_str, "SubmitOrder Error");
                            if (tres.TaskCompleted) { _response_processor.DeactivateAccountResponder(acct.AccountId); }
                            return false;
                        }
                    case OrderType.Stop:
                        if (order.TransactionType == TransactionType.Sell || order.TransactionType == TransactionType.Cover)
                        {
                            r = _orderbook.SubmitPositionStopLossOrder(order, acct);
                            return submitOrderHandleResult(order, r, acct.AccountId, tres.TaskCompleted, out orderId);
                        }
                        else
                        {
                            rejectOrder(order, out orderId);
                            _error_str = "Unknown stop order transaction type '" + ott + "'.";
                            _log.captureError(_error_str, "SubmitOrder Error");
                            if (tres.TaskCompleted) { _response_processor.DeactivateAccountResponder(acct.AccountId); }
                            return false;
                        }
                    default:
                        rejectOrder(order, out orderId);
                        _error_str ="Unknown order type '" + order.OrderType + "'.";
                        _log.captureError(_error_str, "SubmitOrder Error");
                        if (tres.TaskCompleted) { _response_processor.DeactivateAccountResponder(acct.AccountId); }
                        return false;
                }//end of switch()
            }//end of try{}
            catch (Exception e)
            {
                rejectOrder(order, out orderId);
                _log.captureException(e);
                _error_str ="Unhandled exception : " + e.Message;
                _log.captureError(_error_str, "SubmitOrder Error");
                if (tres != null && tres.TaskCompleted && acct != null) { _response_processor.DeactivateAccountResponder(acct.AccountId); }
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
                _log.captureError(_error_str,"CancelOrder Error");
                return false;
            }
            return true;
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
                FXClientObjectResult<double> res = _orderbook.GetMarginAvailable();
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
                if(bo==null)
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
                _log.captureError(_error_str,  "GetOpenOrders Error");
                return null;
            }
        }

        public int GetShares(Symbol symbol)
        {
            try
            {
                clearError();
                _log.captureDebug("GetShares(symbol='" + symbol.Name + "') called.");

                return _orderbook.GetSymbolShares(symbol.Name);
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
            if (_opts.LogTradeErrorsEnabled)
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
            if (_opts.LogTradeErrorsEnabled)
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
