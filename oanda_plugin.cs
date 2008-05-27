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

/*
FIXES:
  * orders price bounds
    * currently using limitprice, but this conflicts with some order types
      * need a better way to transport the bound price point
    * currently bounds range is a plugin option only
      * with a better transport mechanism this could be on a per order basis.
    * what event does oanda fire if a (market/limit) order is rejected due to a bounds violation?
 
 * fix account handling
    * curently using the exchange field as the transport
      * for getting an order specific account
    * verify handler/account needs (list<accounthandler> or just a single object?)
    * bind _orderbook.Book to pair/account not just pair
   orderbook is a dict<acct,bsrl>
     bsrl is a dict<sym,bprl>
       brpl is a dict <posid,bpr>
         bpr is a dict<order_id,TradeRecord)
    * handle transaction responses based on pair/account
    * fix & verify all broker accont status functions
    * etc...
 
 TESTING:
 * order testing (limit/market/stop/target - open/close/cancel/modify/hit)
    * real values and invalid/unset values
    * all RE api calls work and have the proper result at oanda...
    * all order events at oanda have the proper reaction by RE...
 * position testing (same / different directions, same / different symbols)
    * when a market order is submitted and there are existing open shares in any position record
    the plugin should match/fill/close the pending market order when transaction notices for the affected open orders come in
    also the affected traderecord/positionrecord need to be updated
*/
namespace RightEdgeOandaPlugin
{
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

    public class OAPluginOptions
    {
        public OAPluginOptions() { }
        public OAPluginOptions(OAPluginOptions src) { Copy(src); }
        
        public void Copy(OAPluginOptions src)
        {
            OAPluginOptions rsrc = src;
            if (src == null) { rsrc = new OAPluginOptions(); }
            _do_weekend_filter = rsrc._do_weekend_filter;
            _use_bounds = rsrc._use_bounds;
            _log_errors = rsrc._log_errors;

            _log_re_in = rsrc._log_re_in;
            _log_re_out = rsrc._log_re_out;
            _log_oa_in = rsrc._log_oa_in;
            _log_oa_out = rsrc._log_oa_out;

            _log_debug = rsrc._log_debug;
            _log_fname = rsrc._log_fname;
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
        public bool loadRESettings(SerializableDictionary<string, string> settings)
        {
            try
            {
                if (settings.ContainsKey("LogFileName"))          { _log_fname = settings["LogFileName"]; }
                if (settings.ContainsKey("GameServerEnabled"))    { _use_game = bool.Parse(settings["GameServerEnabled"]); }
                if (settings.ContainsKey("BoundsEnabled")) { _use_bounds = bool.Parse(settings["BoundsEnabled"]); }
                
                if (settings.ContainsKey("LogErrorsEnabled")) { _log_errors = bool.Parse(settings["LogErrorsEnabled"]); }

                if (settings.ContainsKey("LogOandaSend")) { _log_oa_out = bool.Parse(settings["LogOandaSend"]); }
                if (settings.ContainsKey("LogOandaReceive")) { _log_oa_in = bool.Parse(settings["LogOandaReceive"]); }
                if (settings.ContainsKey("LogRightEdgeSend")) { _log_re_out = bool.Parse(settings["LogRightEdgeSend"]); }
                if (settings.ContainsKey("LogRightEdgeReceive")) { _log_re_in = bool.Parse(settings["LogRightEdgeReceive"]); }

                if (settings.ContainsKey("LogUnknownEventsEnabled")) { _log_unknown_events = bool.Parse(settings["LogUnknownEventsEnabled"]); }

                if (settings.ContainsKey("LogExceptionsEnabled")) { _log_exceptions = bool.Parse(settings["LogExceptionsEnabled"]); }
                if (settings.ContainsKey("LogDebugEnabled")) { _log_debug = bool.Parse(settings["LogDebugEnabled"]); }
                if (settings.ContainsKey("ShowErrorsEnabled")) { _show_errors = bool.Parse(settings["ShowErrorsEnabled"]); }
                
                if (settings.ContainsKey("WeekendFilterEnabled")) { _do_weekend_filter = bool.Parse(settings["WeekendFilterEnabled"]); }
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
        public bool saveRESettings(ref SerializableDictionary<string, string> settings)
        {
            settings["LogFileName"] = _log_fname;
            settings["GameServerEnabled"] = _use_game.ToString();
            settings["BoundsEnabled"] = _use_bounds.ToString();
            settings["LogErrorsEnabled"] = _log_errors.ToString();

            settings["LogOandaSend"] = _log_oa_out.ToString();
            settings["LogOandaReceive"] = _log_oa_in.ToString();
            settings["LogRightEdgeSend"] = _log_re_out.ToString();
            settings["LogRightEdgeReceive"] = _log_re_in.ToString();

            settings["LogUnknownEventsEnabled"] = _log_unknown_events.ToString();

            settings["LogExceptionsEnabled"] = _log_exceptions.ToString();
            settings["LogDebugEnabled"] = _log_debug.ToString();
            settings["ShowErrorsEnabled"] = _show_errors.ToString();
            settings["WeekendFilterEnabled"] = _do_weekend_filter.ToString();
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

        private string _log_fname = "C:\\RightEdgeOandaPlugin.log";
        [Description("Set this to the file name for logging."), Category("Logging"), Editor(typeof(FilePickUITypeEditor), typeof(UITypeEditor))]
        public string LogFileName { set { _log_fname = value; } get { return (_log_fname); } }

        private bool _log_errors = true;
        [Description("Set this to true to enable logging of errors."), Category("Logging")]
        public bool LogErrorsEnabled { set { _log_errors = value; } get { return (_log_errors); } }

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

        private bool _do_weekend_filter = true;
        [Description("Set this to true to enable the weekend data filter on historic data downloads."), Category("Data Filter")]
        public bool WeekendFilterEnabled { set { _do_weekend_filter = value; } get { return (_do_weekend_filter); } }

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

    public enum IDType { Other, Stop, Target, Close };

    public class IDString
    {

        public IDString() { }
        public IDString(string s) { ID = s; }
        public IDString(IDType t, int onum, int snum) { _type = t; _order_num = onum; _sub_num = snum; }

        private string typeString()
        {
            switch (_type)
            {
                case IDType.Other: return "";
                case IDType.Close: return "close";
                case IDType.Target: return "ptarget";
                case IDType.Stop: return "pstop";
                default:
                    throw new OAPluginException("Unknown IDString type prefix '" + _type + "'.");
            }
        }

        public string ID
        {
            get { return (((_type == IDType.Other) ? "" : (_type==IDType.Other?"":(typeString()+"-"))) + _order_num.ToString() + (_sub_num == 0 ? "" : ("-" + _sub_num.ToString()))); }
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
        public IDType Type { set { _type = value; } get { return (_type); } }
        private int _order_num;
        public int Num { set { _order_num = value; } get { return (_order_num); } }
        private int _sub_num=0;
        public int SubNum { set { _sub_num = value; } get { return (_sub_num); } }
    }
    
    #region Trade Execution Records Classes

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
    public class OrderRecord
    {
        public OrderRecord() { }
        public OrderRecord(BrokerOrder bo) { _order = bo;}

        private BrokerOrder _order = null;
        public BrokerOrder BrokerOrder { set { _order = value; } get { return (_order); } }

        private string _fill_id = null;
        public string FillId { set { _fill_id = value; } get { return (_fill_id); } }

        private int _fill_qty = 0;
        public int FillQty { set { _fill_qty = value; } get { return (_fill_qty); } }

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
    public class TradeRecord
    {
        public TradeRecord() { }

        private IDString _id = null;
        public IDString OrderID { set { _id = value; } get { return (_id); } }

        private OrderRecord _open_order = null;
        public OrderRecord openOrder { set { _open_order = value; } get { return (_open_order); } }

        public OrderRecord _close_order = null;
        public OrderRecord closeOrder { set { _close_order = value; } get { return (_close_order); } }
    }

    [Serializable]
    public class TradeRecordList : SerializableDictionary<IDString, TradeRecord>
    {//key is parsed orderid of tr.openorder

    }

    [Serializable]
    public class BrokerPositionRecord : BrokerPosition
    {
        public BrokerPositionRecord() { }
        public BrokerPositionRecord(string id) { _id = id; }

        private TradeRecordList _tr_dict = new TradeRecordList();
        public TradeRecordList TradeRecords { set { _tr_dict = value; } get { return (_tr_dict); } }

        private string _id;
        public string ID { set { _id = value; } get { return (_id); } }

        private int _stop_num=1;
        public int StopNumber { set { _stop_num = value; } get { return (_stop_num); } }

        private int _target_num=1;
        public int TargetNumber { set { _target_num = value; } get { return (_target_num); } }

        private OrderRecord _stop_order = null;
        public OrderRecord StopOrder { set { _stop_order = value; } get { return (_stop_order); } }

        private OrderRecord _target_order = null;
        public OrderRecord TargetOrder { set { _target_order = value; } get { return (_target_order); } }

        private OrderRecord _close_order = null;
        public OrderRecord CloseOrder { set { _close_order = value; } get { return (_close_order); } }
    }

    [Serializable]
    public class BrokerPositionRecords : SerializableDictionary<string, BrokerPositionRecord>
    {
        public BrokerPositionRecords() { }

        private PositionType _dir;
        public PositionType Direction { set { _dir = value; } get { return (_dir); } }

        public BrokerPositionRecord getPosition(string id)
        {
            if (!ContainsKey(id))
            {
                throw new OAPluginException("ERROR : Unable to locate position record for id '" + id + "'.");
            }
            return(this[id]);
        }
        public int getTotalSize()
        {//sum of all open filled market/limit sizes
            int n=0;
            foreach (string bpr_key in Keys)
            {
                BrokerPositionRecord bpr = this[bpr_key];
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

    public class OrderBook : ContextBoundObject
    {
        private SerializableDictionary<string, BrokerPositionRecords> _book = new SerializableDictionary<string, BrokerPositionRecords>();
        public SerializableDictionary<string, BrokerPositionRecords> Book { set { _book = value; } get { return (_book); } }
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

        public static Account convertExchangeToAccount(fxClient fx_client, string exchange)
        {
            System.Collections.ArrayList acct_list = fx_client.User.GetAccounts();
            int act_i = 0;
            if (!string.IsNullOrEmpty(exchange))
            {
                act_i = int.Parse(exchange);
                if (act_i < 0 || act_i >= acct_list.Count)
                {
                    bool found = false;
                    for (int i = 0; i < acct_list.Count; i++)
                    {
                        if (((Account)acct_list[i]).AccountId == act_i)
                        {
                            act_i = i;
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        throw new OAPluginException("Unable to translate exchange string '" + exchange + "' into an account.");
                    }
                }
            }
            return((Account)acct_list[act_i]);
        }
        public static Fill generateFillFromTransaction(Transaction t)
        {
            Fill fill = new Fill();
            fill.FillDateTime = t.Timestamp;
            fill.Price = new Price(t.Price, t.Price);
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
            double sp=hpoint.Open.Ask - hpoint.Open.Bid;
            double v=hpoint.Min.Ask - hpoint.Min.Bid;
            if(v>sp){sp=v;}
            v=hpoint.Max.Ask - hpoint.Max.Bid;
            if(v>sp){sp=v;}
            v=hpoint.Close.Ask - hpoint.Close.Bid;
            if(v>sp){sp=v;}

            //setup bid/ask off the candle close and the largest detected spread
            nb.Bid = cpoint.Close - (0.5*sp);
            nb.Ask = cpoint.Close + (0.5*sp);

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

    #region fxClient event managers
    public class RateTicker : fxRateEvent
    {
        public RateTicker(Symbol sym, OandAPlugin parent) : base(new fxPair(sym.Name)) { _symbol = sym; _parent = parent; }

        private OandAPlugin _parent;

        private Symbol _symbol;
        public Symbol Symbol { get { return (_symbol); } }

        public int TickCount = 0;
        public double High=0.0;
        public double Low=999999.99;

        public override void handle(fxEventInfo ei, fxEventManager em)
        {
            TickCount++;
            _parent.handleRateTicker(this,(fxRateEventInfo)ei, em);
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
        public AccountResponder(OandAPlugin p) : base() { _parent = p; }

        private OandAPlugin _parent=null;
        
        public override void handle(fxEventInfo ei, fxEventManager em)
        {
            _parent.handleAccountResponder(this,(fxAccountEventInfo)ei,em);
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

        public bool IsOpen { get { return (_fs!=null); } }

        private string _fname = null;
        public string FileName { get { return (_fname); } set { closeLog(); _fname = value; } }

        private FileStream _fs = null;

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
            if (! IsOpen){openLog();}

            string msg = DateTime.Now.ToString() + " [" + Thread.CurrentThread.ManagedThreadId + "] : " + message + "\n";
            byte[] msg_bytes = new UTF8Encoding(true).GetBytes(msg);
            if(!Monitor.TryEnter(_fs,1000))
            {
                throw new OAPluginException("Unable to acquire lock on log file stream.");
            }

            try
            {
            _fs.Write(msg_bytes, 0, msg_bytes.Length);
            _fs.Flush();
            }
            catch(Exception e )
            {
                throw new OAPluginException("", e);
            }
            finally{Monitor.Exit(_fs);}
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
            writeMessage("  RECEIVE RE ORDER : OrderID='" + order.OrderId + "' PosID='" + order.PositionID + "' Shares='" + order.Shares + "' Transaction='" + order.TransactionType + "' Type='" + order.OrderType + "' State='" + order.OrderState + "'.");
        }
        public void captureREIn(string s)
        {
            if (!_log_re_in) { return; }
            writeMessage("  RECEIVE RE ORDER : " + s);
        }
        public void captureREOut(BrokerOrder order,Fill fill,string s)
        {
            if (!_log_re_out) { return; }

            writeMessage(s);
            writeMessage("  SEND RE ORDER : OrderID='" + order.OrderId + "' PosID='" + order.PositionID + "' Shares='" + order.Shares + "' Transaction='" + order.TransactionType + "' Type='" + order.OrderType + "' State='" + order.OrderState + "'.");

            string n = "";
            if (fill != null)
            { n = "  SEND RE FILL : " + fill.FillDateTime + " Qty='" + fill.Quantity + "' AccountPrice='" + fill.Price.AccountPrice + "' SymbolPrice='" + fill.Price.SymbolPrice + "'."; }
            else
            { n = "  SEND RE FILL : (null)"; }
            writeMessage(n);
            
            
        }

        public void captureOAIn(Transaction trans)
        {
            if (!_log_oa_in) { return; }
            writeMessage("  RECEIVE OA EVENT : " + trans.Timestamp + " {" + trans.Base + "/" + trans.Quote + "} " + trans.Description + " [id='" + trans.TransactionNumber + "' link='" + trans.Link + "'].");
        }
        public void captureOAOut(Account acct, LimitOrder lo, string s)
        {
            if (!_log_oa_out) { return; }
            writeMessage("  SEND OA " + s + " LIMIT : Id='" + lo.Id + "' pair='" + lo.Pair + "' units='" + lo.Units + "' price='" + lo.Price + "'");
        }
        public void captureOAOut(Account acct, MarketOrder mo, string s)
        {
            if (!_log_oa_out) { return; }
            writeMessage("  SEND OA " + s + " MARKET : Id='" + mo.Id + "' pair='" + mo.Pair + "' units='" + mo.Units + "'");
        }

        public void captureUnknownEvent(Transaction trans)
        {
            if(!_log_no_match){return;}
            writeMessage("  NOMATCH : " + trans.Timestamp + " {" + trans.Base + "/" + trans.Quote + "} " + trans.Description + " [id='" + trans.TransactionNumber + "' link='" + trans.Link + "'].");
        }

        public void captureDebug(string m)
        {
            if (!_log_debug) { return; }
            writeMessage(m);
        }

        public void captureError(string message, string title)
        {
            if (_log_errors){writeMessage(message);}
            if (_show_errors)
            {
                MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }    
    }

    public class OandAPlugin : IService, IBarDataRetrieval, ITickRetrieval, IBroker
    {
        public OandAPlugin() { }
        ~OandAPlugin() { }

        private fxClient _fx_client = null;
        private OAPluginOptions _opts = null;

        #region Logging & Debugging
        private static PluginLog _log = new PluginLog();
        private void logOrderBookCounts(string s)
        {
            _log.captureDebug(s);
            _log.captureDebug("  orderbook has " + _orderbook.Book.Count + " symbols");
            foreach (string pos_key in _orderbook.Book.Keys)
            {
                BrokerPositionRecords tbprl = _orderbook.Book[pos_key];
                _log.captureDebug("    position list[" + pos_key + "] has " + tbprl.Count + " positions");
                foreach (string bprl_key in tbprl.Keys)
                {
                    BrokerPositionRecord tbpr = tbprl[bprl_key];
                    _log.captureDebug("      position record[" + bprl_key + "] has " + tbpr.TradeRecords.Count + " trades");
                }
            }
            return;
        }
        private void captureDisconnect(SessionException oase,string title)
        {
            _log.captureException(oase);
            _error_str = "Service disconnected.";
            _log.captureError(_error_str,title);
            _fx_client = null;
            //FIX ME <-- if/when RightEdge implements an event for connection state changes, the call to RE should go here...
        }
        #endregion

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

        public bool ShowCustomSettingsForm(ref SerializableDictionary<string, string> settings)
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

        public bool Initialize(SerializableDictionary<string, string> settings)
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
            return r;
        }

        // Implements connection to a service functionality.
        // RightEdge will call this function before requesting
        // service data.  Return true if the connection is
        // successful, otherwise, false.
        public bool Connect(ServiceConnectOptions connectOptions)
        {
            try
            {
                clearError();
                _log.captureDebug("Connect() called.\n--------------------");
                if (_fx_client != null)
                {
                    _error_str = "Connect called on existing fxclient";
                    _log.captureError(_error_str,  "Connect Error");
                    return false;
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
                        _error_str = "Connect() received an unknown ServiceConnectOptions parameter value.";
                        _log.captureError(_error_str,  "Connect Error");
                        return false;
                }
                if (_opts.GameServerEnabled) { _fx_client = new fxGame(); }
                else
                {
                    _fx_client = new fxTrade();
                    //...
                    _error_str = "contact oanda when ready to turn on live";
                    _log.captureError(_error_str,  "Connect Error");
                    return false;
                }

                try
                {
                    _fx_client.WithRateThread = wrt;
                    _fx_client.WithKeepAliveThread = wka;
                    _fx_client.Login(_username, _password);
                }
                catch (SessionException oase)
                {
                    captureDisconnect(oase, "Connect Error");
                    return false;
                }
                catch (OAException oae)
                {
                    _log.captureException(oae);
                    _error_str = "login failed : " + oae.Message;
                    _log.captureError(_error_str,  "Connect Error");
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                _log.captureException(e);
                _error_str = "Unhandled exception : " + e.Message;
                _log.captureError(_error_str,  "Connect Error");
                return false;
            }
        }

        // Implements disconnection from a service.
        // RightEdge will call this function before ending
        // data requests.  Return true if the disconnection is
        // successful, otherwise, false.
        public bool Disconnect()
        {
            try
            {
                clearError();
                //_log.captureDebug("Disconnect() called.");
                if (_fx_client == null) { return true; }
                if (_fx_client.IsLoggedIn)
                {
                    logOrderBookCounts("disconnecting");
                    try
                    {
                        _fx_client.Logout();
                    }
                    catch (SessionException oase)
                    {
                        captureDisconnect(oase,"Disconnect Error");
                        return false;
                    }
                    catch (OAException oae)
                    {
                        _log.captureException(oae);
                        _error_str = "Error disconnecting from oanda : '" + oae.Message + "'"; ;
                        _log.captureError(_error_str,  "Disconnect Error");
                        return false;
                    }
                }
                _fx_client = null;
                return true;
            }
            catch (Exception e)
            {
                _log.captureException(e);
                _error_str = "Unhandled exception : " + e.Message;
                _log.captureError(_error_str,  "Disconnect Error");
                return false;
            }
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
            clearError();
            return OandAUtils.supportedIntervals();
        }

        // This function is called to finally retrieve the data from
        // source.
        public List<BarData> RetrieveData(Symbol symbol, int frequency, DateTime startDate, DateTime endDate, BarConstructionType barConstruction)
        {
            try
            {
                clearError();
                Interval interval = OandAUtils.convertToInterval(frequency);
                CustomBarFrequency cbf = OandAUtils.convertToCustomBarFrequency(frequency);

                //calculate available end date based on Now() and 500 bars@interval for the start
                DateTime availableEnd = OandAUtils.frequencyRoundToStart(DateTime.UtcNow, cbf);
                DateTime availableStart = OandAUtils.frequencyIncrementTime(availableEnd, cbf, -500);

                //validate the input date range overlaps the available range
                if (startDate > availableEnd || endDate < availableStart)
                {
                    _error_str = "No data available for the requested time period.";
                    _log.captureError(_error_str,  "RetrieveData Error");
                    return null;
                }

                int num_ticks = 500;

                List<BarData> list = new List<BarData>();

                System.Collections.ArrayList hal;
                try
                {
                    hal = _fx_client.RateTable.GetHistory(new fxPair(symbol.Name), interval, num_ticks);
                }
                catch (SessionException oase)
                {
                    captureDisconnect(oase,"RetrieveData Error");
                    return null;
                }
                catch (OAException oae)
                {
                    _log.captureException(oae);
                    _error_str = "Error retrieving history : '" + oae.Message + "'.";
                    _log.captureError(_error_str,  "RetrieveData Error");
                    return null;
                }

                bool do_weekend_filter = _opts.WeekendFilterEnabled;
                DayOfWeek weekend_start_day = _opts.WeekendStartDay;
                DayOfWeek weekend_end_day = _opts.WeekendEndDay;
                TimeSpan weekend_start_time = _opts.WeekendStartTime;
                TimeSpan weekend_end_time = _opts.WeekendEndTime;

                System.Collections.IEnumerator iEnum = hal.GetEnumerator();
                while (iEnum.MoveNext())
                {
                    fxHistoryPoint hp = (fxHistoryPoint)iEnum.Current;
                    DateTime hpts = hp.Timestamp;

                    if (hpts < startDate) { continue; }

                    if (do_weekend_filter && (hpts.DayOfWeek >= weekend_start_day || hpts.DayOfWeek <= weekend_end_day))
                    {
                        bool drop_bar = true;
                        if (hpts.DayOfWeek == weekend_start_day && hpts.TimeOfDay < weekend_start_time)
                        { drop_bar = false; }

                        if (hpts.DayOfWeek == weekend_end_day && hpts.TimeOfDay >= weekend_end_time)
                        { drop_bar = false; }

                        if (drop_bar) { continue; }
                    }

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
            if (tick.time.Month == 1 && tick.time.Day == 1 && (tick.time.Year == 1 || tick.time.Year == 2001))
            {
                _log.captureDebug("BAD TICK : time='" + tick.time + "' price='" + tick.price + "' type='" + tick.tickType + "' size='" + tick.size + "'");
                return;
            }
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
            fxEventManager em;
            try
            {
                try
                {
                    em = _fx_client.RateTable.GetEventManager();

                    foreach (RateTicker oldrt in _rate_tickers)
                    { em.remove(oldrt); }
                    _rate_tickers.Clear();
                }
                catch (SessionException oase)
                {
                    captureDisconnect(oase,"SetWatchedSymbols Error");
                    return false;
                }
                catch (OAException oae)
                {
                    _log.captureException(oae);
                    _error_str = "Error clearing watched symbols : '" + oae.Message + "'";
                    _log.captureError(_error_str,  "SetWatchedSymbols Error");
                    return false;
                }

                foreach (Symbol sym in symbols)
                {
                    RateTicker rt = new RateTicker(sym, this);
                    _rate_tickers.Add(rt);
                    try
                    {
                        em.add(rt);
                    }
                    catch (SessionException oase)
                    {
                        captureDisconnect(oase,"SetWatchedSymbols Error");
                        return false;
                    }
                    catch (OAException oae)
                    {
                        _log.captureException(oae);
                        _error_str = "Error setting watch on symbol '" + sym.ToString() + "' : '" + oae.Message + "'";
                        _log.captureError(_error_str,  "SetWatchedSymbols Error");
                        return false;
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                _log.captureException(e);
                _error_str = "Unhandled exception : " + e.Message;
                _log.captureError(_error_str,  "SetWatchedSymbols Error");
                return false;
            }
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

        public void SetAccountState(BrokerAccountState accountState)
        {
            // This function is called before Connect to notify the broker
            // of the list of orders that the system expects are pending,
            // and the positions it expects are open.
            int x = 42;
            x += 1;
        }
        #endregion

        #region orderbok operations
        public bool SubmitOrder(BrokerOrder order, out string orderId)
        {
            orderId = string.Empty;
            try
            {
                clearError();
                _log.captureDebug("SubmitOrder() called.");
                _log.captureREIn(order);
                bool r = false;
                Account acct;
                try
                {
                    acct = OandAUtils.convertExchangeToAccount(_fx_client, order.OrderSymbol.Exchange);
                }
                catch (SessionException oase)
                {
                    captureDisconnect(oase,"SubmitOrder Error");
                    return false;
                }
                catch (Exception e)
                {
                    _log.captureException(e);
                    _error_str = "Error getting oanda account object : '" + e.Message + "'.";
                    _log.captureError(_error_str,  "SubmitOrder Error");
                    return false;
                }

                if (!_accont_responders.ContainsKey(acct.AccountId))
                {
                    AccountResponder ar = new AccountResponder(this);
                    try
                    {
                        fxEventManager em = acct.GetEventManager();
                        em.add(ar);
                    }
                    catch (SessionException oase)
                    {
                        captureDisconnect(oase,"SubmitOrder Error");
                        return false;
                    }
                    catch (OAException oae)
                    {
                        _log.captureException(oae);
                        _error_str = "Error getting oanda account event manager : '" + oae.Message + "'.";
                        _log.captureError(_error_str,  "SubmitOrder Error");
                        return false;
                    }
                    _accont_responders[acct.AccountId] = ar;
                }

                TransactionType ott = order.TransactionType;
                switch (order.OrderType)
                {
                    case OrderType.Market:
                        if (ott == TransactionType.Sell || ott == TransactionType.Cover)
                        {
                            r = submitCloseOrder(order, acct);
                            if (r) { orderId = order.OrderId; }
                            return (r);
                        }
                        else if (ott == TransactionType.Buy || ott == TransactionType.Short)
                        {
                            r = submitMarketOrder(order, acct);
                            if (r) { orderId = order.OrderId; }
                            return (r);
                        }
                        else
                        {
                            _error_str = "Unknown market order transaction type '" + ott + "'.";
                            _log.captureError(_error_str,  "SubmitOrder Error");
                            return false;
                        }
                    case OrderType.Limit:
                        if (order.TransactionType == TransactionType.Sell || order.TransactionType == TransactionType.Cover)
                        {
                            r = submitPositionTargetProfitOrder(order, acct);
                            if (r) { orderId = order.OrderId; }
                            return (r);
                        }
                        else if (ott == TransactionType.Buy || ott == TransactionType.Short)
                        {
                            r = submitLimitOrder(order, acct);
                            if (r) { orderId = order.OrderId; }
                            return (r);
                        }
                        else
                        {
                            _error_str = "Unknown limit order transaction type '" + ott + "'.";
                            _log.captureError(_error_str,  "SubmitOrder Error");
                            return false;
                        }
                    case OrderType.Stop:
                        if (order.TransactionType == TransactionType.Sell || order.TransactionType == TransactionType.Cover)
                        {
                            r = submitPositionStopLossOrder(order, acct);
                            if (r) { orderId = order.OrderId; }
                            return (r);
                        }
                        else
                        {
                            _error_str = "Unknown stop order transaction type '" + ott + "'.";
                            _log.captureError(_error_str,  "SubmitOrder Error");
                            return false;
                        }
                    default:
                        _error_str = "Unknown order type '" + order.OrderType + "'.";
                        _log.captureError(_error_str,  "SubmitOrder Error");
                        return false;
                }
            }
            catch (Exception e)
            {
                _log.captureException(e);
                _error_str = "Unhandled exception : " + e.Message;
                _log.captureError(_error_str,  "SubmitOrder Error");
                return false;
            }
        }

        // Clear all open orders that haven't been processed yet
        public bool CancelAllOrders()
        {
            try
            {
                clearError();
                _log.captureDebug("CancelAllOrders() called.");
                _log.captureREIn("CANCEL ALL");
                foreach (string pos_key in _orderbook.Book.Keys)
                {
                    foreach (string bpr_key in _orderbook.Book[pos_key].Keys)
                    {
                        BrokerPositionRecord bpr = _orderbook.Book[pos_key][bpr_key];
                        foreach (IDString tr_key in bpr.TradeRecords.Keys)
                        {
                            TradeRecord tr = bpr.TradeRecords[tr_key];

                            BrokerOrder bro = tr.openOrder.BrokerOrder;
                            if (bro.OrderType != OrderType.Limit)
                            {//FIX ME <-- is this right? should close all close ALL pending AND open??
                                _log.captureDebug("CloseAllOrders skipping open order type '" + bro.OrderState + "' id '" + bro.OrderId + "'.");
                                continue;
                            }

                            Account acct;
                            LimitOrder lo = new LimitOrder();
                            int id_num = tr.OrderID.Num;
                            switch (bro.OrderState)
                            {
                                case BrokerOrderState.Submitted:
                                    #region oanda close limit order
                                    try
                                    {
                                        acct = OandAUtils.convertExchangeToAccount(_fx_client, bro.OrderSymbol.Exchange);

                                        if (!acct.GetOrderWithId(lo, id_num))
                                        {
                                            _error_str = "ERROR : Unable to locate the corresponding oanda broker order for id '" + id_num + "'.";
                                            _log.captureError(_error_str,  "CancelOrder Error");
                                            return false;
                                        }
                                        sendOAClose(acct, lo);
                                    }
                                    catch (SessionException oase)
                                    {
                                        captureDisconnect(oase, "CancelOrder Error");
                                        return false;
                                    }
                                    catch (OAException oae)
                                    {
                                        _log.captureException(oae);
                                        _error_str = "Error closing oanda order : '" + oae.Message + "'.";
                                        _log.captureError(_error_str,  "CancelOrder Error");
                                        return false;
                                    }
                                    #endregion
                                    break;
                                //FIX ME - what other states need action here???
                                default:
                                    _log.captureDebug("CloseAllOrders skipping open order state '" + bro.OrderState + "' id '" + bro.OrderId + "'.");
                                    break;
                            }
                        }
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                _log.captureException(e);
                _error_str = "Unhandled exception : " + e.Message;
                _log.captureError(_error_str,  "CancelAllOrders Error");
                return false;
            }
        }

        // Cancel or clear a particular order if it hasn't been processed yet.
        public bool CancelOrder(string orderId)
        {
            try
            {
                clearError();
                _log.captureDebug("CancelOrder() called : id='" + orderId + "'");
                _log.captureREIn("CANCEL ORDER '" + orderId + "'");

                Account acct;
                IDString oid = new IDString(orderId);
                bool is_pos_id = false;
                if (oid.Type != IDType.Other) { is_pos_id = true; }
                int id_num = oid.Num;

                foreach (string pos_key in _orderbook.Book.Keys)
                {
                    foreach (string pair in _orderbook.Book[pos_key].Keys)
                    {
                        BrokerPositionRecord bpr = _orderbook.Book[pos_key][pair];

                        #region handle position order cancel
                        if (is_pos_id && bpr.ID == id_num.ToString())
                        {
                            int orders_sent = 0;
                            BrokerOrder bro = null;
                            switch (oid.Type)
                            {//handle update to the stop/target order here...there were no trades/orders to adjust
                                case IDType.Stop:
                                    #region update position stop
                                    bro = bpr.StopOrder.BrokerOrder;
                                    bro.StopPrice = 0.0;
                                    bro.OrderState = BrokerOrderState.PendingCancel;
                                    fireOrderUpdated(bro, null, "cancelling pstop");
                                    try
                                    {
                                        acct = OandAUtils.convertExchangeToAccount(_fx_client, bpr.StopOrder.BrokerOrder.OrderSymbol.Exchange);
                                    }
                                    catch (SessionException oase)
                                    {
                                        captureDisconnect(oase, "CancelOrder Error");
                                        return false;
                                    }
                                    catch (OAException oae)
                                    {
                                        _log.captureException(oae);
                                        _error_str = "Error getting account information from oanda api. " + oae.Message;
                                        _log.captureError(_error_str,  "CancelOrder Error");
                                        return false;
                                    }
                                    if (!submitStopOrders(bpr, bro, acct, out orders_sent)) { return false; }
                                    if (orders_sent == 0)
                                    {
                                        bro.OrderState = BrokerOrderState.Cancelled;
                                        fireOrderUpdated(bro, null, "pstop cancelled");
                                        bpr.StopOrder = null;
                                        removePositionRecord(_orderbook.Book[pos_key], bpr, pos_key);
                                    }
                                    return true;
                                    #endregion
                                case IDType.Target:
                                    #region update position target
                                    bro = bpr.TargetOrder.BrokerOrder;
                                    bro.LimitPrice = 0.0;
                                    bro.OrderState = BrokerOrderState.PendingCancel;
                                    fireOrderUpdated(bro, null, "cancelling ptarget");
                                    try
                                    {
                                        acct = OandAUtils.convertExchangeToAccount(_fx_client, bpr.TargetOrder.BrokerOrder.OrderSymbol.Exchange);
                                    }
                                    catch (SessionException oase)
                                    {
                                        captureDisconnect(oase, "CancelOrder Error");
                                        return false;
                                    }
                                    catch (OAException oae)
                                    {
                                        _log.captureException(oae);
                                        _error_str = "Error getting account information from oanda api. " + oae.Message;
                                        _log.captureError(_error_str,  "CancelOrder Error");
                                        return false;
                                    }
                                    if (!submitTargetOrders(bpr, bro, acct, out orders_sent)) { return false; }
                                    if (orders_sent == 0)
                                    {
                                        bpr.TargetOrder.BrokerOrder.OrderState = BrokerOrderState.Cancelled;
                                        fireOrderUpdated(bpr.TargetOrder.BrokerOrder, null, "cancel ptarget");
                                        bpr.TargetOrder = null;
                                        clearStopped(bpr);
                                        removePositionRecord(_orderbook.Book[pos_key], bpr, pos_key);
                                    }
                                    return true;
                                    #endregion
                                default:
                                    _error_str = "Unable to process order id prefix order ID '" + oid.ID + "'.";
                                    _log.captureError(_error_str,  "CancelOrder Error");
                                    return false;
                            }
                        }
                        #endregion

                        #region handle specific order cancel
                        foreach (IDString tr_key in bpr.TradeRecords.Keys)
                        {
                            TradeRecord tr = bpr.TradeRecords[tr_key];
                            BrokerOrder bro = tr.openOrder.BrokerOrder;
                            if (tr.OrderID.Num == id_num)
                            {//this one...
                                #region verify specified order is an unfilled limit order
                                if (bro.OrderType != OrderType.Limit)
                                {
                                    _error_str = "Canceling an unknown order type '" + bro.OrderType + "'.";
                                    _log.captureError(_error_str,  "CancelOrder Error");
                                    return false;
                                }


                                switch (bro.OrderState)
                                {
                                    case BrokerOrderState.Submitted:
                                        break;
                                    //FIX ME - what other states are ok here???
                                    default:
                                        _error_str = "Canceling an order in an unknown state '" + bro.OrderState + "'.";
                                        _log.captureError(_error_str,  "CancelOrder Error");
                                        return false;
                                }
                                #endregion

                                #region oanda close order
                                LimitOrder lo = new LimitOrder();
                                try
                                {
                                    acct = OandAUtils.convertExchangeToAccount(_fx_client, bro.OrderSymbol.Exchange);

                                    if (!acct.GetOrderWithId(lo, id_num))
                                    {
                                        _error_str = "ERROR : Unable to locate the corresponding oanda broker order for id '" + id_num + "'.";
                                        _log.captureError(_error_str,  "CancelOrder Error");
                                        return false;
                                    }
                                    sendOAClose(acct, lo);
                                }
                                catch (SessionException oase)
                                {
                                    captureDisconnect(oase, "CancelOrder Error");
                                    return false;
                                }
                                catch (OAException oae)
                                {
                                    _log.captureException(oae);
                                    _error_str = "Error closing oanda order : '" + oae.Message + "'.";
                                    _log.captureError(_error_str,  "CancelOrder Error");
                                    return false;
                                }
                                #endregion

                                return true;
                            }
                        }
                        #endregion
                    }
                }
                _error_str = "Unable to find an open order to cancel or order ID '" + id_num + "' never existed.";
                _log.captureError(_error_str,  "CancelOrder Error");
                return false;
            }
            catch (Exception e)
            {
                _log.captureException(e);
                _error_str = "Unhandled exception : " + e.Message;
                _log.captureError(_error_str,  "CancelOrder Error");
                return false;
            }
        }
        #endregion

        #region account status
        // Informs RightEdge of the amount available
        // for buying or shorting.
        public double GetBuyingPower()
        {
            clearError();
            try
            {
                Account acct = OandAUtils.convertExchangeToAccount(_fx_client, "");
                return acct.MarginAvailable();
            }
            catch (SessionException oase)
            {
                captureDisconnect(oase, "GetBuyingPower Error");
                return (-1.0);
            }
            catch (OAException e)
            {
                _log.captureException(e);
                _error_str = "Unable to get account margin information : '" + e.Message + "'.";
                _log.captureError(_error_str,  "GetBuyingPower Error");
                return (-1.0);
            }
            catch (Exception e)
            {
                _log.captureException(e);
                _error_str = "Unhandled exception : " + e.Message;
                _log.captureError(_error_str,  "GetBuyingPower Error");
                throw new OAPluginException(_error_str, e);
            }
        }

        public double GetMargin()
        {
            clearError();
            try//	Return the amount of cash in the margin account.
            {
                Account acct = OandAUtils.convertExchangeToAccount(_fx_client, "");
                return acct.MarginAvailable();
            }
            catch (SessionException oase)
            {
                captureDisconnect(oase, "GetMargin Error");
                return (-1.0);
            }
            catch (OAException e)
            {
                _log.captureException(e);
                _error_str = "Unable to get account margin information : '" + e.Message + "'.";
                _log.captureError(_error_str,"GetMargin Error");
                return (-1.0);
            }
            catch (Exception e)
            {
                _log.captureException(e);
                _error_str = "Unhandled exception : " + e.Message;
                _log.captureError(_error_str,  "GetBuyingPower Error");
                throw new OAPluginException(_error_str, e);
            }
        }

        public double GetShortedCash()
        {
            clearError();
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
                _log.captureDebug("GetOpenOrder('" + id + "') called.");

                foreach (string sym_key in _orderbook.Book.Keys)
                {
                    BrokerPositionRecords bprl = _orderbook.Book[sym_key];
                    foreach (string bpr_key in bprl.Keys)
                    {
                        BrokerPositionRecord bpr = bprl[bpr_key];
                        if (bpr.StopOrder != null && bpr.StopOrder.BrokerOrder.OrderId == id)
                        { return (bpr.StopOrder.BrokerOrder); }
                        else if (bpr.TargetOrder != null && bpr.TargetOrder.BrokerOrder.OrderId == id)
                        { return (bpr.TargetOrder.BrokerOrder); }

                        foreach (IDString tr_key in bpr.TradeRecords.Keys)
                        {
                            TradeRecord tr = bpr.TradeRecords[tr_key];
                            if (tr.openOrder.BrokerOrder.OrderId == id)
                            { return (tr.openOrder.BrokerOrder); }
                        }
                    }
                }
                _error_str = "ERROR : Unable to locate an open order record for id : '" + id + "'.";
                _log.captureError(_error_str,  "GetOpenOrder Error");
                return null;
            }
            catch (Exception e)
            {
                _log.captureException(e);
                _error_str = "Unhandled exception : " + e.Message;
                _log.captureError(_error_str,  "GetOpenOrder Error");
                return null;
            }
        }

        // returns a copy of the currently open orders.
        public List<BrokerOrder> GetOpenOrders()
        {
            try
            {
                clearError();
                List<BrokerOrder> list = new List<BrokerOrder>();

                foreach (string sym_key in _orderbook.Book.Keys)
                {
                    BrokerPositionRecords bprl = _orderbook.Book[sym_key];
                    foreach (string bpr_key in bprl.Keys)
                    {
                        BrokerPositionRecord bpr = bprl[bpr_key];
                        if (bpr.StopOrder != null) { list.Add(bpr.StopOrder.BrokerOrder.Clone()); }
                        if (bpr.TargetOrder != null) { list.Add(bpr.TargetOrder.BrokerOrder.Clone()); }
                        foreach (IDString tr_key in bpr.TradeRecords.Keys)
                        {
                            TradeRecord tr = bpr.TradeRecords[tr_key];
                            list.Add(tr.openOrder.BrokerOrder.Clone());
                        }
                    }
                }
                return list;
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
            clearError();
            if (!_orderbook.Book.ContainsKey(symbol.Name)) { return (0); }
            BrokerPositionRecords bprl = _orderbook.Book[symbol.Name];
            return bprl.getTotalSize();
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

        #region Broker backend members

        //FIX ME <-- does there really need to be an instance of the responder for every account in use???
        //key: account number
        private Dictionary<int, AccountResponder> _accont_responders = new Dictionary<int, AccountResponder>();

        //key: symbol name (aka pair string)
        private OrderBook _orderbook = new OrderBook();

        //key: symbol name (aka pair string)
        private SerializableDictionary<string, List<FillRecord>> _fill_queue = new SerializableDictionary<string, List<FillRecord>>();


        #region Account Event Responder and Helpers

        public void handleAccountResponder(AccountResponder ar, fxAccountEventInfo aei, fxEventManager em)
        {
            Transaction trans = aei.Transaction;
            try
            {
                _log.captureDebug("handleAccountResponder() called.");
                _log.captureOAIn(trans);

                #region get associated Broker Position Record List
                string pair = trans.Base + "/" + trans.Quote;
                if (!_orderbook.Book.ContainsKey(pair))
                {
                    RESendNoMatch(ar,aei,em);
                    return;
                }
                BrokerPositionRecords bprl = _orderbook.Book[pair];
                #endregion

                string desc = trans.Description;
                int link_id = trans.Link;

                #region scan position record list for a matching trade record
                foreach (string pos_key in bprl.Keys)
                {
                    BrokerPositionRecord pos = bprl[pos_key];
                    foreach (IDString tr_key in pos.TradeRecords.Keys)
                    {
                        TradeRecord tr = pos.TradeRecords[tr_key];
                        
                        int id_num = tr.OrderID.Num;
                        
                        int fill_id_num = 0;
                        if (tr.openOrder != null && !string.IsNullOrEmpty(tr.openOrder.FillId))
                        { fill_id_num = int.Parse(tr.openOrder.FillId); }

                        if (trans.TransactionNumber == id_num)
                        {//transaction matched an open order directly...
                            #region open order direct id match
                            if (desc == "Buy Order" || //long limit order response
                                desc == "Sell Order")  //short limit order response
                            {//the fill on a limit comes in under a linked id, 
                                return;//so do nothing here
                            }
                            else if (desc == "Buy Market" || //long market order response
                                     desc == "Sell Market")  //short market order response
                            {
                                Fill fill = OandAUtils.generateFillFromTransaction(trans);
                                RESendFilledOrder(fill, tr.openOrder.BrokerOrder, "market order open");
                                return;
                            }
                            #endregion
                        }
                        else if (link_id != 0 && (link_id == id_num || link_id == fill_id_num))
                        {//transaction is linked to the open order
                            #region open order linked id match
                            if (desc == "Order Fulfilled")
                            {//this transaction is a notice that a limit order has been filled
                                //make sure this openOrder is an unfilled limit
                                if (tr.openOrder.BrokerOrder.OrderType != OrderType.Limit)
                                {//what the heck?!?
                                    _error_str = "Order fullfilment event received on non-limit order id '" + tr.openOrder.BrokerOrder.OrderId + "' type '" + tr.openOrder.BrokerOrder.OrderType + "'.";
                                    _log.captureError(_error_str,  "handleAccountResponder Error");
                                    throw new OAPluginException(_error_str);
                                }

                                if (tr.openOrder.BrokerOrder.OrderState != BrokerOrderState.Submitted)
                                {//the order was filled at oanda and is in a bad way in RE....
                                    _error_str = "TBI - this should be more thorough...not all states are unusable, and those that are need an Update()";
                                    _log.captureError(_error_str,  "handleAccountResponder Error");
                                    throw new OAPluginException(_error_str);
                                }


                                if (!_fill_queue.ContainsKey(pair))
                                {//no queued up fills for this symbol!!
                                    _error_str = "No fill record in the queue for symbol '" + pair + "'.";
                                    _log.captureError(_error_str,  "handleAccountResponder Error");
                                    throw new OAPluginException(_error_str);
                                }

                                //do a lookup on the queue of fill records for a fill that matches (size, etc..)this openOrder
                                foreach (FillRecord fr in _fill_queue[pair])
                                {
                                    if (fr.Fill.Quantity == tr.openOrder.BrokerOrder.Shares)
                                    {
                                        tr.openOrder.FillId = fr.Id;

                                        //remove the fill record from the queue
                                        _fill_queue[pair].Remove(fr);
                                        if (_fill_queue[pair].Count == 0) { _fill_queue.Remove(pair); }

                                        //update the openOrder as filled
                                        RESendFilledOrder(fr.Fill, tr.openOrder.BrokerOrder, "limit order fullfilment");
                                        return;
                                    }
                                }
                                _error_str = "No fill record for the order id '" + tr.openOrder.BrokerOrder.OrderId + "' symbol '" + pair + "'.";
                                _log.captureError(_error_str,  "handleAccountResponder Error");
                                throw new OAPluginException(_error_str);
                            }
                            else if (desc == "Cancel Order")
                            {
                                tr.openOrder.BrokerOrder.OrderState = BrokerOrderState.Cancelled;
                                fireOrderUpdated(tr.openOrder.BrokerOrder, null, "handleAccountResponder() : cancel openOrder");
                                if (pos.StopOrder == null && pos.TargetOrder == null && pos.CloseOrder == null) { tr.openOrder = null; }
                                removeTradeRecord(bprl, pos, pair, tr);
                                return;
                            }
                            else if (desc == "Close Trade")
                            {//FIX ME -- this procedure needs to account for tr.closeOrder possibly being null already
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
                                    nbo.OrderId = "close-" + tr.openOrder.BrokerOrder.OrderId;

                                    tr.closeOrder = new OrderRecord(nbo);

                                    //send re close submitted before sending the fill
                                    fireOrderUpdated(tr.closeOrder.BrokerOrder, null, "external close trade");
                                } 

                                Fill fill = OandAUtils.generateFillFromTransaction(trans);

                                if (pos.CloseOrder == null)
                                { RESendFilledOrder(fill, tr.closeOrder.BrokerOrder, "close trade"); }
                                else
                                {
                                    BrokerOrderState fill_state = RESendFilledOrder(fill, pos.CloseOrder.BrokerOrder, "close position trade");
                                    if (fill_state == BrokerOrderState.Filled)
                                    { pos.CloseOrder = null; }
                                }
                                tr.closeOrder = null;

                                if (pos.StopOrder == null && pos.TargetOrder == null && tr.closeOrder == null) { tr.openOrder = null; }
                                removeTradeRecord(bprl, pos, pair, tr);
                                return;
                            }
                            else if (desc == "Close Position")
                            {
                                if(tr.closeOrder == null)
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
                                    nbo.OrderId = "close-" + tr.openOrder.BrokerOrder.OrderId;
                                    
                                    tr.closeOrder = new OrderRecord(nbo);
                                    
                                    //send re close submitted before sending the fill
                                    fireOrderUpdated(tr.closeOrder.BrokerOrder,null,"external close position");
                                }
                                
                                Fill fill = OandAUtils.generateFillFromTransaction(trans);

                                if (pos.CloseOrder == null)
                                { RESendFilledOrder(fill, tr.closeOrder.BrokerOrder, "close position"); }
                                else
                                {
                                    BrokerOrderState fill_state = RESendFilledOrder(fill, pos.CloseOrder.BrokerOrder, "close position position");
                                    if (fill_state == BrokerOrderState.Filled)
                                    { pos.CloseOrder = null; }
                                }
                                
                                tr.closeOrder = null;

                                if (pos.StopOrder == null && pos.TargetOrder == null && tr.closeOrder == null) { tr.openOrder = null; }
                                removeTradeRecord(bprl, pos, pair, tr);
                                return;
                            }
                            else if (desc == "Modify Trade")
                            {//modify response...
                                double sl = trans.Stop_loss;
                                double tp = trans.Take_profit;

                                if (sl != tr.openOrder.StopPrice)
                                {//stop changed
                                    tr.openOrder.StopPrice = sl;
                                    if (pos.StopOrder.BrokerOrder.OrderState == BrokerOrderState.PendingCancel)
                                    {//then count 'cancel fills'and update stopOrder when done
                                        pos.StopOrder.FillQty += (int)tr.openOrder.BrokerOrder.Shares;
                                        if (pos.StopOrder.FillQty >= pos.StopOrder.BrokerOrder.Shares)
                                        {
                                            pos.StopOrder.BrokerOrder.OrderState = BrokerOrderState.Cancelled;
                                            fireOrderUpdated(pos.StopOrder.BrokerOrder, null, "Cancel stop");
                                            pos.StopOrder = null;
                                            removePositionRecord(bprl, pos, pos_key);
                                        }
                                    }
                                    return;
                                }
                                else if (tp != tr.openOrder.TargetPrice)
                                {//stop changed
                                    tr.openOrder.TargetPrice = tp;
                                    if (pos.TargetOrder.BrokerOrder.OrderState == BrokerOrderState.PendingCancel)
                                    {//then count 'cancel fills'and update stopOrder when done
                                        pos.TargetOrder.FillQty += (int)tr.openOrder.BrokerOrder.Shares;
                                        if (pos.TargetOrder.FillQty >= pos.TargetOrder.BrokerOrder.Shares)
                                        {
                                            pos.TargetOrder.BrokerOrder.OrderState = BrokerOrderState.Cancelled;
                                            fireOrderUpdated(pos.TargetOrder.BrokerOrder, null, "Cancel target");
                                            pos.TargetOrder = null;
                                            removePositionRecord(bprl, pos, pos_key);
                                        }
                                    }
                                    return;
                                }
                                else
                                {
                                    _error_str = "Oanda 'Modify Trade' event type changed something other than the stop loss price on order '" + tr.openOrder.BrokerOrder.OrderId + "'.";
                                    _log.captureError(_error_str,  "handleAccountResponder Error");
                                    throw new OAPluginException(_error_str);
                                }
                            }
                            else if (desc == "Stop Loss")
                            {
                                //now the stop has been hit...add up the stop_qty...
                                Fill fill = OandAUtils.generateFillFromTransaction(trans);

                                tr.openOrder.StopHit = true;
                                pos.StopOrder.FillQty += fill.Quantity;

                                fill.Quantity = pos.StopOrder.FillQty;
                                if (pos.StopOrder.FillQty >= pos.StopOrder.BrokerOrder.Shares)
                                {
                                    RESendFilledOrder(fill, pos.StopOrder.BrokerOrder, "stop loss");
                                    pos.StopOrder = null;
                                    removePositionRecord(bprl, pos, pos_key);
                                }
                                return;
                            }
                            else if (desc == "Take Profit")
                            {
                                //now the target has been hit...add up the trgt_qty...
                                Fill fill = OandAUtils.generateFillFromTransaction(trans);

                                pos.TargetOrder.FillQty += fill.Quantity;
                                tr.openOrder.TargetHit = true;

                                fill.Quantity = pos.TargetOrder.FillQty;
                                if (pos.TargetOrder.FillQty >= pos.TargetOrder.BrokerOrder.Shares)
                                {
                                    RESendFilledOrder(fill, pos.TargetOrder.BrokerOrder, "target");
                                    pos.TargetOrder = null;
                                    removePositionRecord(bprl, pos, pos_key);
                                }
                                return;
                            }
                            #endregion
                        }
                    }
                }
                #endregion

                //a limit order has been filled, once filled oanda gives it a new id
                if (desc == "Buy Order Filled" || desc == "Sell Order Filled")
                {//add the new id and the fill to the queue of fill records
                    Fill fill = OandAUtils.generateFillFromTransaction(trans);
                    addFillrecord(pair, fill, trans.TransactionNumber);
                    return;//wait for the "Order Fulfilled" event to finalize the original limit order
                }

                RESendNoMatch(ar, aei, em);
                return;
            }
            catch (Exception e)
            {
                _log.captureException(e);
                _error_str = "Unhandled exception : " + e.Message;
                _log.captureError(_error_str,  "handleAccountResponder Error");
                throw new OAPluginException(_error_str, e);
            }
        }

        private BrokerOrderState RESendFilledOrder(Fill fill, BrokerOrder order, string s)
        {
            order.Fills.Add(fill);

            int tq = 0;
            foreach (Fill f in order.Fills)
            { tq += f.Quantity; }

            if (tq >= order.Shares) { order.OrderState = BrokerOrderState.Filled; }
            else { order.OrderState = BrokerOrderState.PartiallyFilled; }

            fireOrderUpdated(order, fill, "fill on " + s);
            return order.OrderState;
        }
        private void RESendNoMatch(AccountResponder ar, fxAccountEventInfo aei, fxEventManager em)
        {
            //this receives ALL ACCOUNT EVENTS!!! It doesn't matter where or how they originate.
            //If ANY connected client triggers an event, it will be sent to ALL clients
            Transaction trans = aei.Transaction;
            _log.captureUnknownEvent(trans);
            
        }
        #endregion

        #region backend record helpers
        private BrokerPositionRecords getPositionList(string pair)
        {
            if (!_orderbook.Book.ContainsKey(pair))
            {
                _error_str = "No position found for '" + pair + "'.";
                _log.captureError(_error_str,"getPositionList Error");
                throw new OAPluginException(_error_str);
            }
            return (_orderbook.Book[pair]);
        }

        //not really needed??? limit orders are valid in any directions
        //transaction events should fully resolve orders
        //the same way oanda resolves them...
        private bool checkNewPositionDirection(BrokerOrder order)
        {
            PositionType order_dir = (order.TransactionType == TransactionType.Short) ? PositionType.Short : PositionType.Long;
            if (_orderbook.Book.ContainsKey(order.OrderSymbol.Name))
            {
                BrokerPositionRecords bprl = _orderbook.Book[order.OrderSymbol.Name];
                bool match_existing = false;
                foreach (string bpr_key in bprl.Keys)
                {
                    if (bprl[bpr_key].ID == order.PositionID) { match_existing = true; break; }
                }
                if (bprl.Count>0 && !match_existing)
                {//new position in an existing position list must match the position direction
                    if (order_dir != bprl.Direction) {return false;}
                }
            }
            return true;//null or empty position list, or safe oder
        }

        private void clearStopped(BrokerPositionRecord bpr)
        {//clear orders that have stophit==true
            foreach (IDString tr_key in bpr.TradeRecords.Keys)
            {
                TradeRecord tr = bpr.TradeRecords[tr_key];
                if (tr.openOrder.StopHit)
                {
                    tr.openOrder = null;
                    tr.closeOrder = null;
                }
            }
        }
        private void clearTarget(BrokerPositionRecord bpr)
        {
            foreach (IDString tr_key in bpr.TradeRecords.Keys)
            {
                TradeRecord tr = bpr.TradeRecords[tr_key];
                if (tr.openOrder.TargetHit)
                {
                    tr.openOrder = null;
                    tr.closeOrder = null;
                }
            }
        }

        private void removePositionRecord(BrokerPositionRecords bprl, BrokerPositionRecord bpr, string pair)
        {
            foreach (IDString tr_key in bpr.TradeRecords.Keys)
            {
                removeTradeRecord(bprl, bpr, pair, bpr.TradeRecords[tr_key]);
            }
        }

        private void addTradeRecord(BrokerOrder order)
        {
            PositionType order_dir = (order.TransactionType == TransactionType.Short) ? PositionType.Short : PositionType.Long;
            BrokerPositionRecord p = null;
            if (!_orderbook.Book.ContainsKey(order.OrderSymbol.Name))
            {
                BrokerPositionRecords pl = new BrokerPositionRecords();
                p = new BrokerPositionRecord();

                p.Direction = order_dir;
                p.Symbol = order.OrderSymbol;
                p.ID = order.PositionID;

                pl[p.ID] = p;
                pl.Direction = order_dir;
                _orderbook.Book[order.OrderSymbol.Name] = pl;
            }
            else
            {
                BrokerPositionRecords bprl = _orderbook.Book[order.OrderSymbol.Name];
                foreach (string bpr_key in bprl.Keys)
                {
                    BrokerPositionRecord bpr = bprl[bpr_key];
                    if (bpr.ID == order.PositionID)
                    { p = bpr; break; }
                }
                if (p == null)
                {//adding a new position to the existing position list
                    p = new BrokerPositionRecord();

                    p.Direction = (order.TransactionType == TransactionType.Short) ? PositionType.Short : PositionType.Long;
                    p.Symbol = order.OrderSymbol;
                    p.ID = order.PositionID;

                    bprl[p.ID]=p;
                }
            }

            TradeRecord tr = new TradeRecord();
            tr.openOrder=new OrderRecord(order);

            tr.OrderID=new IDString(order.OrderId);
            p.TradeRecords[tr.OrderID] = tr;
        }
        private void removeTradeRecord(BrokerPositionRecords bprl, BrokerPositionRecord bpr, string pair, TradeRecord tr)
        {
            if (tr.openOrder == null && tr.closeOrder == null)
            {
                if (!Monitor.TryEnter(bpr.TradeRecords, 1000))
                {
                    _error_str = "ERROR: Unable to acquire trade records lock.";
                    _log.captureError(_error_str,  "removeTradeRecord error");
                    throw new OAPluginException(_error_str);
                }
                bpr.TradeRecords.Remove(tr.OrderID);
                Monitor.Exit(bpr.TradeRecords);
            }

            if (bpr.TradeRecords.Count == 0 && bpr.StopOrder == null && bpr.TargetOrder == null && bpr.CloseOrder == null)
            { bprl.Remove(bpr.ID); }
            if (bprl.Count == 0)
            { _orderbook.Book.Remove(pair); }

            logOrderBookCounts("removeTradeRecord - post call counts");
            return;
        }
        
        private void addFillrecord(string pair, Fill fill, int id)
        {
            if (!_fill_queue.ContainsKey(pair))
            {
                _fill_queue[pair] = new List<FillRecord>();
            }
            _fill_queue[pair].Add(new FillRecord(fill,id.ToString()));
        }
        #endregion

        #region backend order submission helpers
        private bool submitLimitOrder(BrokerOrder order, Account acct)
        {
            fxPair oa_pair = new fxPair(order.OrderSymbol.ToString());
            LimitOrder lo = new LimitOrder();

            lo.Base = oa_pair.Base;
            lo.Quote = oa_pair.Quote;

            lo.Units = (int)order.Shares;
            if (order.TransactionType == TransactionType.Short)
            { lo.Units = -1 * lo.Units; }


            //FIX ME<-- extract an order specific bounds value from the order/symbol tags...
            double slippage=_opts.Bounds;//use the broker value as the fallback

            if (_opts.BoundsEnabled)//always honor the broker enabled setting
            {
                lo.HighPriceLimit = order.LimitPrice + 0.5 * slippage;
                lo.LowPriceLimit = order.LimitPrice - 0.5 * slippage;
            }
            lo.Price = order.LimitPrice;

            if (!order.GoodTillCanceled)
            {
                //lo.Duration = ;
            }


            try
            {
                sendOAExecute(acct, lo);
            }
            catch (SessionException oase)
            {
                captureDisconnect(oase, "submitLimitOrder error");
                //FIX ME <-- order failed submission to the broker....
                return false;
            }
            catch (OAException e)
            {
                _log.captureException(e);
                _error_str = "ERROR : Unable to submit limit order to oanda the servers : '" + e.Message + "'.";
                _log.captureError(_error_str,  "submitLimitOrder error");
                order.OrderState = BrokerOrderState.Rejected;//FIX ME <-- order failed submission to the broker....
                return false;
            }
            order.OrderState = BrokerOrderState.Submitted;
            order.OrderId = lo.Id.ToString();

            addTradeRecord(order);
            return true;
        }
        private bool submitMarketOrder(BrokerOrder order, Account acct)
        {
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

            try
            {
                sendOAExecute(acct, mo);
            }
            catch (SessionException oase)
            {
                captureDisconnect(oase, "submitMarketOrder Error");
                //FIX ME <-- order failed submission to the broker....
                return false;
            }
            catch (OAException e)
            {
                _log.captureException(e);
                _error_str = "ERROR : unable to submit market order to oanda the servers : '" + e.Message + "'.";
                _log.captureError(_error_str,  "submitMarketOrder Error");
                order.OrderState = BrokerOrderState.Rejected;//FIX ME <-- order failed submission to the broker....
                return false;
            }
            order.OrderState = BrokerOrderState.Submitted;
            order.OrderId = mo.Id.ToString();

            addTradeRecord(order);
            return true;
        }
        private bool submitCloseOrder(BrokerOrder order, Account acct)
        {
            BrokerPositionRecords bprl;
            BrokerPositionRecord cp;
            try
            {
                bprl = getPositionList(order.OrderSymbol.Name);
                cp = bprl.getPosition(order.PositionID);
            }
            catch (Exception e)
            {
                _log.captureException(e);
                _error_str = "ERROR : Unable to locate close order's position record : '" + e.Message + "'.";
                _log.captureError(_error_str,  "submitCloseOrder Error");
                return false;
            }
            if (!Monitor.TryEnter(cp.TradeRecords,1000))
            {
                _error_str = "ERROR : unable to acquire trade records lock.";
                _log.captureError(_error_str,  "submitCloseOrder Error");
                return false;
            }
            try
            {

                order.OrderState = BrokerOrderState.Submitted;
                order.OrderId = "close-" + order.PositionID;
                cp.CloseOrder = new OrderRecord(order);

                foreach (IDString tr_key in cp.TradeRecords.Keys)
                {
                    TradeRecord tr = cp.TradeRecords[tr_key];
                    MarketOrder cmo = new MarketOrder();
                    int id_num;

                    OrderType ot = tr.openOrder.BrokerOrder.OrderType;
                    if (ot == OrderType.Limit)
                    { id_num = string.IsNullOrEmpty(tr.openOrder.FillId) ? 0 : int.Parse(tr.openOrder.FillId); }
                    else if (ot == OrderType.Market)
                    { id_num = tr.OrderID.Num; }
                    else
                    {
                        Monitor.Exit(cp.TradeRecords);
                        _error_str = "ERROR : Unable to process close order on open order type '" + ot + "'.";
                        _log.captureError(_error_str,  "submitCloseOrder Error");
                        return false;
                    }

                    try
                    {
                        if (!acct.GetTradeWithId(cmo, id_num))
                        {
                            Monitor.Exit(cp.TradeRecords);
                            order.OrderState = BrokerOrderState.Invalid;//FIX ME 
                            _error_str = "ERROR : Unable to locate trade id '" + id_num + "' for position '" + cp.ID + "' at oanda.";
                            _log.captureError(_error_str,  "submitCloseOrder Error");
                            return false;
                        }
                        sendOAClose(acct, cmo);
                    }
                    catch (SessionException oase)
                    {
                        Monitor.Exit(cp.TradeRecords);
                        captureDisconnect(oase, "submitCloseOrder Error");
                        return false;
                    }
                    catch (OAException e)
                    {
                        Monitor.Exit(cp.TradeRecords);
                        _log.captureException(e);
                        _error_str = "ERROR : Unable to close trade id '" + id_num + "' for position '" + cp.ID + "' at oanda : '" + e.Message + "'.";
                        order.OrderState = BrokerOrderState.Rejected;//FIX ME
                        _log.captureError(_error_str,  "submitCloseOrder error");
                        return false;
                    }
                    BrokerOrder bro = new BrokerOrder();
                    bro.OrderId = "close-" + cmo.Id;
                    bro.SubmittedDate = DateTime.Now;
                    bro.Shares = (long)cmo.Units;
                    bro.PositionID = order.PositionID;
                    bro.OrderSymbol = order.OrderSymbol;
                    bro.OrderType = order.OrderType;
                    bro.TransactionType = order.TransactionType;
                    tr.closeOrder = new OrderRecord(bro);
                }
            }
            catch (Exception e)
            {
                throw new OAPluginException("", e);
            }
            finally { Monitor.Exit(cp.TradeRecords); }
            return true;
        }

        private bool submitPositionStopLossOrder(BrokerOrder order, Account acct)
        {
            string oa_pair = order.OrderSymbol.ToString();
            BrokerPositionRecords bprl;
            BrokerPositionRecord bpr;

            try
            {
                bprl = getPositionList(oa_pair);
                bpr = bprl.getPosition(order.PositionID);
            }
            catch (Exception e)
            {
                _log.captureException(e);
                _error_str = "ERROR : Unable to locate stop order's position record : '" + e.Message + "'.";
                _log.captureError(_error_str,  "submitPositionStopLossOrder Error");
                return false;
            }

            IDString n_id = new IDString(IDType.Stop, int.Parse(order.PositionID), bpr.StopNumber++);
            order.OrderId = n_id.ID;
            int orders_sent = 0;
            bool r = submitStopOrders(bpr, order, acct, out orders_sent);
            return r;
        }
        private bool submitStopOrders(BrokerPositionRecord bpr, BrokerOrder order, Account acct, out int orders_sent)
        {
            orders_sent = 0;
            double stop_price = order.StopPrice;

            if (bpr.StopOrder == null) { bpr.StopOrder = new OrderRecord(order); }

            #region send stop orders
            //for each traderecord set the openorder stop price at oanda

            foreach (IDString tr_key in bpr.TradeRecords.Keys)
            {
                TradeRecord tr = bpr.TradeRecords[tr_key];

                int id_num = tr.OrderID.Num;

                if (tr.openOrder.BrokerOrder.OrderType == OrderType.Limit && tr.openOrder.BrokerOrder.OrderState == BrokerOrderState.Submitted)
                {
                    #region modify pending orders
                    LimitOrder lo = new LimitOrder();
                    try
                    {
                        if (!acct.GetOrderWithId(lo, id_num))
                        {
                            _error_str = "ERROR : Unable to locate oanda limit order for pending order '" + id_num + "'.";
                            _log.captureError(_error_str,  "submitStopOrders Error");
                            return false;
                        }

                        if (lo.stopLossOrder.Price == stop_price) { continue; }
                        lo.stopLossOrder.Price = stop_price;

                        sendOAModify(acct, lo);
                    }
                    catch (SessionException oase)
                    {
                        captureDisconnect(oase, "submitStopOrders Error");
                        return false;
                    }
                    catch (OAException e)
                    {
                        _log.captureException(e);
                        _error_str = "ERROR : Unable to modify Oanda limit order  : '" + e.Message + "'."; ;
                        _log.captureError(_error_str,  "submitStopOrders Error");
                        return false;
                    }
                    orders_sent++;
                    #endregion
                }
                else if (tr.openOrder.BrokerOrder.OrderState == BrokerOrderState.Filled && !tr.openOrder.TargetHit && !tr.openOrder.StopHit)
                {
                    #region modify active orders
                    MarketOrder mo = new MarketOrder();
                    try
                    {
                        if (!acct.GetTradeWithId(mo, id_num))
                        {
                            _error_str = "ERROR : Unable to locate oanda trade (market order) for filled order '" + id_num + "'.";
                            _log.captureError(_error_str,  "submitStopOrders Error");
                            return false;
                        }

                        if (mo.stopLossOrder.Price == stop_price) { continue; }
                        mo.stopLossOrder.Price = stop_price;

                        sendOAModify(acct, mo);
                    }
                    catch (SessionException oase)
                    {
                        captureDisconnect(oase, "submitStopOrders Error");
                        return false;
                    }
                    catch (OAException e)
                    {
                        _log.captureException(e);
                        _error_str = "ERROR : Unable to modify Oanda market order : '" + e.Message + "'.";
                        _log.captureError(_error_str,  "submitStopOrders Error");
                        return false;
                    }
                    orders_sent++;
                    #endregion
                }
                else if (stop_price != 0.0)
                {
                    //unkown target order error
                    _error_str = "ERROR : Unknown open order state for stop modification. {id='" + tr.openOrder.BrokerOrder.OrderId + "' posid='" + tr.openOrder.BrokerOrder.PositionID + "' type='" + tr.openOrder.BrokerOrder.OrderType + "' state='" + tr.openOrder.BrokerOrder.OrderState + "'}";
                    _log.captureError(_error_str,  "submitStopOrders Error");
                    return false;
                }
                //
            }
            #endregion
            return true;
        }
          
        private bool submitPositionTargetProfitOrder(BrokerOrder order, Account acct)
        {
            string oa_pair = order.OrderSymbol.ToString();
            BrokerPositionRecords bprl;
            BrokerPositionRecord bpr;

            try
            {
                bprl = getPositionList(oa_pair);
                bpr = bprl.getPosition(order.PositionID);
            }
            catch (Exception e)
            {
                _log.captureException(e);
                _error_str = "ERROR : Unable to locate target order's position record : '" + e.Message + "'.";
                _log.captureError(_error_str,  "submitPositionTargetProfitOrder Error");
                return false;
            }

            IDString n_id = new IDString(IDType.Target, int.Parse(order.PositionID), bpr.TargetNumber++);
            order.OrderId = n_id.ID;
            int orders_sent = 0;
            bool r = submitTargetOrders(bpr, order, acct, out orders_sent);
            return r;
        }
        private bool submitTargetOrders(BrokerPositionRecord bpr, BrokerOrder order, Account acct, out int orders_sent)
        {
            orders_sent = 0;
            double target_price = order.LimitPrice;

            if (bpr.TargetOrder == null) { bpr.TargetOrder = new OrderRecord(order); }
            
            #region send target orders
            //for each traderecord set the openorder stop price at oanda
            
            foreach (IDString tr_key in bpr.TradeRecords.Keys)
            {
                TradeRecord tr = bpr.TradeRecords[tr_key];

                int id_num = tr.OrderID.Num;

                if (tr.openOrder.BrokerOrder.OrderType == OrderType.Limit && tr.openOrder.BrokerOrder.OrderState == BrokerOrderState.Submitted)
                {
                    #region modify pending orders
                    LimitOrder lo = new LimitOrder();
                    try
                    {
                        if (!acct.GetOrderWithId(lo, id_num))
                        {
                            _error_str = "ERROR : Unable to locate oanda limit order for pending order '" + id_num + "'.";
                            _log.captureError(_error_str,  "submitTargetOrders Error");
                            return false;
                        }

                        if (lo.takeProfitOrder.Price == target_price) { continue; }
                        lo.takeProfitOrder.Price = target_price;

                        sendOAModify(acct, lo);
                    }
                    catch (SessionException oase)
                    {
                        captureDisconnect(oase, "submitTargetOrders Error");
                        return false;
                    }
                    catch (OAException e)
                    {
                        _log.captureException(e);
                        _error_str = "ERROR : Unable to modify Oanda limit order  : '" + e.Message + "'."; ;
                        _log.captureError(_error_str,  "submitTargetOrders Error");
                        return false;
                    }
                    orders_sent++;
                    #endregion
                }
                else if (tr.openOrder.BrokerOrder.OrderState == BrokerOrderState.Filled && !tr.openOrder.TargetHit && !tr.openOrder.StopHit )
                {
                    #region modify active orders
                    MarketOrder mo = new MarketOrder();
                    try
                    {
                        if (!acct.GetTradeWithId(mo, id_num))
                        {
                            _error_str = "ERROR : Unable to locate oanda trade (market order) for filled order '" + id_num + "'.";
                            _log.captureError(_error_str,  "submitTargetOrders Error");
                            return false;
                        }

                        if (mo.takeProfitOrder.Price == target_price) { continue; }
                        mo.takeProfitOrder.Price = target_price;

                        sendOAModify(acct, mo);
                    }
                    catch (SessionException oase)
                    {
                        captureDisconnect(oase, "submitTargetOrders Error");
                        return false;
                    }
                    catch (OAException e)
                    {
                        _log.captureException(e);
                        _error_str = "Unable to modify Oanda market order : '" + e.Message + "'.";
                        _log.captureError(_error_str,  "submitTargetOrders Error");
                        return false;
                    }
                    orders_sent++;
                    #endregion
                }
                else if (target_price != 0.0)
                {
                    //unkown target order error
                    _error_str = "ERROR : Unknown open order state for target modification. {id='" + tr.openOrder.BrokerOrder.OrderId + "' posid='" + tr.openOrder.BrokerOrder.PositionID + "' type='" + tr.openOrder.BrokerOrder.OrderType + "' state='" + tr.openOrder.BrokerOrder.OrderState + "'}";
                    _log.captureError(_error_str,  "submitTargetOrders Error");
                    return false;
                }
                //else target of 0.0 is a cancel

            }
            #endregion
            return true;
        }
        #endregion


        #region logged send to oanda wrappers
        private void sendOAClose(Account acct, LimitOrder lo)
        {
            _log.captureOAOut(acct, lo, "CLOSE");
            acct.Close(lo);
        }
        private void sendOAModify(Account acct, LimitOrder lo)
        {
            _log.captureOAOut(acct, lo, "MODIFY");
            acct.Modify(lo);
        }
        private void sendOAExecute(Account acct, LimitOrder lo)
        {
            _log.captureOAOut(acct, lo, "EXECUTE");
            acct.Execute(lo);
        }

        private void sendOAClose(Account acct, MarketOrder mo)
        {
            _log.captureOAOut(acct, mo, "CLOSE");
            acct.Close(mo);
        }
        private void sendOAModify(Account acct, MarketOrder mo)
        {
            _log.captureOAOut(acct, mo, "MODIFY");
            acct.Modify(mo);
        }
        private void sendOAExecute(Account acct, MarketOrder mo)
        {
            _log.captureOAOut(acct, mo, "EXECUTE");
            acct.Execute(mo);
        }
        #endregion

        #region logged send to right edge wrapper
        private void fireOrderUpdated(BrokerOrder order, Fill fill, string s)
        {
            _log.captureREOut(order, fill, "SEND RE (" + s + ")");
            OrderUpdated(order, fill, s);
        }
        #endregion
        #endregion

        #region IDisposable Members
        // Must be implemented, however, action is
        // not required as is the case here.
        public void Dispose()
        {
        }
        #endregion
    }
}
