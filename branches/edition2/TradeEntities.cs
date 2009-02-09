using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Drawing.Design;
using System.Windows.Forms;
using System.Windows.Forms.Design;

using RightEdge.Common;

namespace RightEdgeOandaPlugin
{
    public enum ValueScaleType { Unset, Fixed, Percentage };

    #region generic versions of the custom property editor form classes
    public interface ICustomComparable
    {
        int CompareValues(ICustomComparable src);
    }
    
    public interface ICustomComparableEditorForm
    {
        ICustomComparable Value { set; get; }
    }
    public interface ICustomComparableDictionaryForm<T>
        where T : ICustomComparable, INamedObject, new()
    {
        IDictionary<string, T> Values { set; get; }
    }


    public class ComparableCustomEditor<T, F> : UITypeEditor
        where T : ICustomComparable, new()
        where F : Form, ICustomComparableEditorForm, new()
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
            F form = new F();
            form.Value = (T)value;
            DialogResult fres = edSvc.ShowDialog(form);
            if (fres != DialogResult.OK) { return value; }
            if (((T)value).CompareValues(form.Value) == 0)
            { return value; }
            else
            { return form.Value; }
        }
    }

    public class CustomComparableDictionaryEditor<T, F> : UITypeEditor
        where T : ICustomComparable,INamedObject, new()
        where F : Form, ICustomComparableDictionaryForm<T>, new()
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
            F form = new F();

            IDictionary<string, T> dict = (IDictionary<string, T>)value;
            form.Values = dict;
            DialogResult fres = edSvc.ShowDialog(form);
            if (fres != DialogResult.OK) { return value; }

            bool changed = true;
            if (form.Values.Count == dict.Count)
            {
                changed = false;
                foreach (string n in form.Values.Keys)
                {
                    if (!dict.ContainsKey(n))
                    { changed = true; break; }
                    if (form.Values[n].CompareValues(dict[n]) != 0)
                    { changed = true; break; }
                }
            }
            if (changed)
            { return form.Values; }
            else
            { return value; }
        }
    }
    #endregion


    #region xml serialization interface and base class
    public interface IXMLFileSerialize
    {
        string FileName { set; get; }

        DateTime LastFileModification { get; }
        DateTime LastFileLoad { get; }

        FunctionResult waitForUpdate(int s);
        FunctionResult waitForUpdate(DateTime prev, int s);

        FunctionResult clear();
        FunctionResult refresh<T>() where T : IXMLFileSerialize, new();
        FunctionResult refresh(Type t);

        FunctionResult saveSettings<T>() where T : IXMLFileSerialize, new();
        FunctionResult saveSettings(Type t);

        FunctionResult loadSettings<T>(string opt_fname) where T : IXMLFileSerialize, new();
        FunctionResult loadSettings(Type t, string opt_fname);

        FunctionResult loadSettings<T>(System.Xml.XmlReader reader) where T : new();
        FunctionResult loadSettings(Type t, System.Xml.XmlReader reader);

        FunctionResult initFromSerialized<T>(T src);
        FunctionResult initFromSerialized(Type t, object src);
    }

    public class XMLFileSerializeBase : IXMLFileSerialize
    {
        public XMLFileSerializeBase()
        {
            _last_modify_ts = DateTime.MinValue;
            _last_load_ts = DateTime.MinValue;
        }

        private string _file_name = string.Empty;
        [XmlIgnore, Browsable(false)]
        public string FileName { set { _file_name = value; } get { return (_file_name); } }

        private DateTime _last_modify_ts;
        [XmlIgnore, Browsable(false)]
        public DateTime LastFileModification { set { _last_modify_ts = value; } get { return (_last_modify_ts); } }
        private DateTime _last_load_ts;
        [XmlIgnore, Browsable(false)]
        public DateTime LastFileLoad { set { _last_load_ts = value; } get { return (_last_load_ts); } }

        public FunctionResult saveSettings<T>() where T : IXMLFileSerialize, new()
        {
            if (string.IsNullOrEmpty(_file_name))
            {
                return FunctionResult.newError("Unable to serialize to empty file name.");
            }
            try
            {
                XmlSerializer mySerializer = new XmlSerializer(typeof(T));
                StreamWriter myWriter = new StreamWriter(_file_name);
                mySerializer.Serialize(myWriter, this);
                myWriter.Close();
                myWriter.Dispose();
                FileInfo fi = new FileInfo(_file_name);
                _last_modify_ts = fi.LastWriteTime;
                return new FunctionResult();
            }
            catch (Exception e)
            {
                return FunctionResult.newError("saveSettings exception : " + e.Message);
            }
        }
        public FunctionResult saveSettings(Type t)
        {
            if (string.IsNullOrEmpty(_file_name))
            {
                return FunctionResult.newError("Unable to serialize to empty file name.");
            }
            try
            {
                XmlSerializer mySerializer = new XmlSerializer(t);
                StreamWriter myWriter = new StreamWriter(_file_name);
                mySerializer.Serialize(myWriter, this);
                myWriter.Close();
                myWriter.Dispose();
                FileInfo fi = new FileInfo(_file_name);
                _last_modify_ts = fi.LastWriteTime;
                return new FunctionResult();
            }
            catch (Exception e)
            {
                return FunctionResult.newError("saveSettings exception : " + e.Message);
            }
        }

        public static FunctionObjectResult<T> newFromSettings<T>(string fname) where T : IXMLFileSerialize, new()
        {
            FunctionObjectResult<T> tres = new FunctionObjectResult<T>();
            T t = new T();
            FunctionResult lres = t.loadSettings<T>(fname);
            if (lres.Error)
            {
                tres.setError("unable to de-serialize and load file '" + fname + "'. " + lres.Message);
                return tres;
            }
            tres.ResultObject = t;
            return tres;
        }
        public static FunctionObjectResult<object> newFromSettings(Type t, string fname)
        {
            FunctionObjectResult<object> tres = new FunctionObjectResult<object>();
            object o = t.Assembly.CreateInstance(t.FullName);

            FunctionResult lres = ((IXMLFileSerialize)o).loadSettings(t, fname);
            if (lres.Error)
            {
                tres.setError("unable to de-serialize and load file '" + fname + "'. " + lres.Message);
                return tres;
            }
            tres.ResultObject = o;
            return tres;
        }

        public virtual FunctionResult initFromSerialized<T>(T src)
        {
            _last_load_ts = DateTime.Now;
            return new FunctionResult();
        }
        public virtual FunctionResult initFromSerialized(Type t, object src)
        {
            _last_load_ts = DateTime.Now;
            return new FunctionResult();
        }

        public virtual FunctionResult clear()
        {
            return (new FunctionResult());
        }
        public FunctionResult refresh<T>() where T : IXMLFileSerialize, new()
        {
            FileInfo fi = new FileInfo(_file_name);

            if (fi.LastWriteTime > _last_modify_ts)
            {
                clear();//FIX ME - there should be a way to undo this if the load fails
                FunctionResult fres = loadSettings<T>(_file_name);
                if (fres.Error) { return fres; }
                _last_modify_ts = fi.LastWriteTime;
            }
            return new FunctionResult();
        }
        public FunctionResult refresh(Type t)
        {
            FileInfo fi = new FileInfo(_file_name);

            if (fi.LastWriteTime > _last_modify_ts)
            {
                clear();//FIX ME - there should be a way to undo this if the load fails
                FunctionResult fres = loadSettings(t, _file_name);
                if (fres.Error) { return fres; }
                _last_modify_ts = fi.LastWriteTime;
            }
            return new FunctionResult();
        }

        public FunctionResult loadSettings<T>(string fname) where T : IXMLFileSerialize, new()
        {
            XmlSerializer mySerializer = new XmlSerializer(typeof(T));
            FileStream myFileStream = null;
            try
            {
                myFileStream = new FileStream(fname, FileMode.Open);
            }
            catch (System.IO.IOException e)
            {
                _file_name = fname;
                return FunctionResult.newError("File Load Error : " + e.Message);
            }
            T t = (T)mySerializer.Deserialize(myFileStream);
            myFileStream.Dispose();
            _file_name = fname;
            return initFromSerialized<T>(t);
        }
        public FunctionResult loadSettings(Type t, string fname)
        {
            XmlSerializer mySerializer = new XmlSerializer(t);
            FileStream myFileStream = null;
            try
            {
                myFileStream = new FileStream(fname, FileMode.Open);
            }
            catch (System.IO.IOException e)
            {
                _file_name = fname;
                return FunctionResult.newError("File Load Error : " + e.Message);
            }
            object o = mySerializer.Deserialize(myFileStream);
            myFileStream.Dispose();
            _file_name = fname;
            return initFromSerialized(t, o);
        }

        public FunctionResult loadSettings<T>(System.Xml.XmlReader reader) where T : new()
        {
            XmlSerializer mySerializer = new XmlSerializer(typeof(T));
            object o = mySerializer.Deserialize(reader);
            return initFromSerialized<T>((T)o);
        }
        public FunctionResult loadSettings(Type t, System.Xml.XmlReader reader)
        {
            XmlSerializer mySerializer = new XmlSerializer(t);
            object o = mySerializer.Deserialize(reader);
            return initFromSerialized(t, o);
        }


        public FunctionResult waitForUpdate(int s)
        {
            FileInfo fi = new FileInfo(FileName);
            return waitForUpdate(fi.LastWriteTime, s);
        }
        public FunctionResult waitForUpdate(DateTime lts, int s)
        {
            FileInfo fi = new FileInfo(FileName);
            int sc = 0;
            do
            {
                if (fi.LastWriteTime > lts)
                { break; }
                System.Threading.Thread.Sleep(1000);
                fi.Refresh();
                sc++;
            } while (sc < s);
            if (sc >= s)
            {
                return FunctionResult.newError("No update found in time alloted.");
            }

            return new FunctionResult();
        }
    }
    #endregion

    #region account values store
    [Serializable]
    public class AccountValue
    {
        public AccountValue() { }

        private string _account_id = string.Empty;
        public string AccountID { get { return _account_id; } set { _account_id = value; } }
        private string _account_name = string.Empty;
        public string AccountName { get { return _account_name; } set { _account_name = value; } }
        private string _account_currency = string.Empty;
        public string AccountCurrency { get { return _account_currency; } set { _account_currency = value; } }

        private double _margin_available = 0.0;
        public double MarginAvailable { get { return _margin_available; } set { _margin_available = value; } }
        private double _margin_used = 0.0;
        public double MarginUsed { get { return _margin_used; } set { _margin_used = value; } }
        private double _margin_rate = 0.0;
        public double MarginRate { get { return _margin_rate; } set { _margin_rate = value; } }
        private DateTime _margin_ts;
        public DateTime MarginTimeStamp { get { return _margin_ts; } set { _margin_ts = value; } }

        private double _balance = 0.0;
        public double Balance { get { return _balance; } set { _balance = value; } }
        private DateTime _balance_ts;
        public DateTime BalanceTimeStamp { get { return _balance_ts; } set { _balance_ts = value; } }
        //....
    }
    [Serializable]
    public class AccountValuesStore : XMLFileSerializeBase
    {
        public AccountValuesStore() { }

        SerializableDictionary<string, AccountValue> _values = new SerializableDictionary<string, AccountValue>();
        public SerializableDictionary<string, AccountValue> Values { get { return _values; } set { _values = value; } }

        public FunctionResult RefreshFromBroker(SystemData sd)
        {
            sd.Broker.GetBuyingPower();
            sd.Broker.GetMargin();

            FunctionResult fres = waitForUpdate(LastFileModification, 10);
            if (fres.Error) { return fres; }

            return refresh<AccountValuesStore>();
        }

        public void SetBalance(string id, string n, string c, double b)
        {
            if (!_values.ContainsKey(id))
            {
                _values.Add(id, new AccountValue());
                _values[id].AccountID = id;
                _values[id].AccountCurrency = c;
            }
            _values[id].AccountName = n;
            _values[id].Balance = b;
            _values[id].BalanceTimeStamp = DateTime.Now;
        }

        public void SetMargin(string id, string n, string c, double ma, double mu, double mr)
        {
            if (!_values.ContainsKey(id))
            {
                _values.Add(id, new AccountValue());
                _values[id].AccountID = id;
                _values[id].AccountCurrency = c;
            }
            _values[id].AccountName = n;
            _values[id].MarginAvailable = ma;
            _values[id].MarginRate = mr;
            _values[id].MarginUsed = mu;
            _values[id].MarginTimeStamp = DateTime.Now;
        }

        public override FunctionResult clear()
        {
            _values.Clear();
            return base.clear();
        }
        public override FunctionResult initFromSerialized<T>(T src)
        {
            if (typeof(AccountValuesStore) == src.GetType())
            {
                AccountValuesStore avs = ((AccountValuesStore)((object)src));
                _values = avs.Values;
            }
            return base.initFromSerialized<T>(src);
        }
        public override FunctionResult initFromSerialized(Type t, object src)
        {
            if (typeof(AccountValuesStore) == t)
            {
                AccountValuesStore avs = (AccountValuesStore)src;
                _values = avs.Values;
            }
            return base.initFromSerialized(t, src);
        }
    }
    #endregion

    #region trade entities
    public class TradeEntityID : ICustomComparable, INamedObject
    {
        public const string Delim = ":";

        public TradeEntityID() { }
        public TradeEntityID(string id) { ParseString(id); }
        public TradeEntityID(string n, string s, PositionType pt)
        { _ename = n; _symbol = s; _pt = pt; }

        public TradeEntityID(TradeEntityID src) { Copy(src); }
        public void Copy(TradeEntityID src)
        {
            _ename = src._ename;
            _symbol = src._symbol;
            _pt = src._pt;
        }

        public static TradeEntityID Parse(string id)
        {
            TradeEntityID eid = new TradeEntityID();
            eid.ParseString(id);
            return eid;
        }
        public void ParseString(string id)
        {
            string[] sres = id.Split(Delim.ToCharArray());
            if (sres.Length != 3)
            {
                throw new ArgumentOutOfRangeException();
            }
            
            _ename = sres[0];
            _symbol = sres[1];
            _pt = (PositionType)Enum.Parse(typeof(PositionType), sres[2]);
            return;
        }
        public static string CreateID(string n, string s, TransactionType tt)
        {
            return n + Delim + s + Delim + OandAUtils.convertToPositionType(tt).ToString();
        }
        public static string CreateID(string n, string s, PositionType pt)
        {
            return n + Delim + s + Delim + pt.ToString();
        }
        public static string CreateID(string n, BrokerOrder order)
        {
            return n + Delim + order.OrderSymbol.Name + Delim + OandAUtils.convertToPositionType(order.TransactionType).ToString();
        }

        [XmlIgnore, Browsable(false)]
        public string ID { get { return (_ename + Delim + _symbol + Delim + _pt.ToString()); } }

        #region id sub values
        private string _ename = string.Empty;
        [DisplayNameAttribute("Entity Name"), Description("The entity name for this entry.")]
        public string EntityName { get { return _ename; } set { _ename = value; } }

        private string _symbol = string.Empty;
        [DisplayNameAttribute("Symbol Name"), Description("The symbol name for this entry.")]
        public string SymbolName { get { return _symbol; } set { _symbol = value; } }

        private PositionType _pt;
        [DisplayNameAttribute("Position Type"), Description("The trading direction for this entry.")]
        public PositionType PositionType { get { return _pt; } set { _pt = value; } }
        #endregion

        #region INamedObject
        [XmlIgnore, Browsable(false)]
        public string Name { get { return ID; } }
        public void SetName(string n)
        {
            Copy(Parse(n));
        }
        #endregion

        #region ICustomComparable
        public int CompareValues(ICustomComparable src)
        {
            TradeEntityID src_eid = (TradeEntityID)src;

            int n = _ename.CompareTo(src_eid._ename);
            if (n != 0) { return n; }
            n = _symbol.CompareTo(src_eid._symbol);
            if (n != 0) { return n; }

            if (_pt > src_eid._pt) { return 1; }
            else if (_pt < src_eid._pt) { return -1; }

            return 0;
        }
#endregion
    }


    [Serializable]
    [Editor(typeof(ComparableCustomEditor<TradeEntity, TradeEntityForm>), typeof(UITypeEditor))]
    [TypeConverter(typeof(NamedConverter))]
    public class TradeEntity : ICustomComparable, INamedObject
    {
        public TradeEntity() { }
        public TradeEntity(TradeEntityID id)
        { _id = id; }
        public TradeEntity(TradeEntityID id, string act)
        { _id = id; _account = act; }
        public TradeEntity(TradeEntityID id, string act, double bspr)
        { _id = id; _account = act; _base_spread = bspr; }
        public TradeEntity(TradeEntityID id, string act, ValueScaleType ubt, double ubv, ValueScaleType lbt, double lbv)
        { _id = id; _account = act; _upper_type = ubt; _upper_bound = ubv; _lower_type = lbt; _lower_bound = lbv; }
        public TradeEntity(TradeEntityID id, string act, ValueScaleType ubt, double ubv, ValueScaleType lbt, double lbv, double bspr)
        { _id = id; _account = act; _upper_type = ubt; _upper_bound = ubv; _lower_type = lbt; _lower_bound = lbv; _base_spread = bspr; }

        public TradeEntity(TradeEntity src) { Copy(src); }

        public void Copy(TradeEntity src)
        {
            _account = src._account;
            _base_spread=src._base_spread;
            _id=src._id;
            _lower_bound=src._lower_bound;
            _lower_type=src._lower_type;
            _order_size=src._order_size;
            _order_size_type=src._order_size_type;
            _upper_bound=src._upper_bound;
            _upper_type=src._upper_type;
        }
        public int CompareValues(ICustomComparable v)
        {
            TradeEntity src = (TradeEntity)v;

            int n = _account.CompareTo(src._account);
            if (n != 0) { return n; }

            if (_base_spread > src._base_spread)
            { return 1; }
            else if (_base_spread < src._base_spread)
            { return -1; }

            n = _id.CompareValues(src._id);
            if (n != 0) { return n; }

            if (_lower_bound > src._lower_bound)
            { return 1; }
            else if (_lower_bound < src._lower_bound)
            { return -1; }

            if (_lower_type > src._lower_type)
            { return 1; }
            else if (_lower_type < src._lower_type)
            { return -1; }
            
            if (_order_size > src._order_size)
            { return 1; }
            else if (_order_size < src._order_size)
            { return -1; }

            if (_order_size_type > src._order_size_type)
            { return 1; }
            else if (_order_size_type < src._order_size_type)
            { return -1; }

            if (_upper_bound > src._upper_bound)
            { return 1; }
            else if (_upper_bound < src._upper_bound)
            { return -1; }

            if (_upper_type > src._upper_type)
            { return 1; }
            else if (_upper_type < src._upper_type)
            { return -1; }

            return 0;
        }

        #region id values
        private TradeEntityID _id = new TradeEntityID();
        [XmlIgnore, Browsable(false)]
        public TradeEntityID EntityID { get { return (_id); } }
        [XmlIgnore, Browsable(false)]
        public string ID { get { return (_id.ID); } }
        
        #region old key values....these should go away
        [Browsable(false)]
        public string EntityName { get { return _id.EntityName; } set { _id.EntityName = value; } }
        [Browsable(false)]
        public string SymbolName { get { return _id.SymbolName; } set { _id.SymbolName = value; } }
        [Browsable(false)]
        public PositionType PositionType { get { return _id.PositionType; } set { _id.PositionType = value; } }
        #endregion
        [XmlIgnore, Browsable(false)]
        public string Name { get { return _id.ID; } }

        public void SetName(string n)
        {
            _id = new TradeEntityID(n);
        }
        #endregion

        #region lookup values
        private string _account = string.Empty;
        [Description("The account this entity should use.")]
        public string Account { get { return _account; } set { _account = value; } }

        private double _base_spread = 0.0;
        [Description("A default spread value, specific to this entity.")]
        public double BaseSpread { get { return _base_spread; } set { _base_spread = value; } }

        #region order sizing
        private double _order_size = 100.0;
        [Description("The order size for this entity.")]
        public double OrderSize { get { return _order_size; } set { _order_size = value; } }

        private ValueScaleType _order_size_type = ValueScaleType.Percentage;
        [Description("The type of the order size.")]
        public ValueScaleType OrderSizeType { get { return _order_size_type; } set { _order_size_type = value; } }

        public int GetOrderSizeValue(AccountValue av, double base_pr)
        {
            switch (_order_size_type)
            {
                case ValueScaleType.Fixed:
                    return (int)Math.Floor(_order_size);

                case ValueScaleType.Percentage:
                    return (int)Math.Floor(((av.MarginAvailable * av.MarginRate) / base_pr) * (_order_size / 100.0));

                case ValueScaleType.Unset:
                default:
                    return -1;
            }
        }
        #endregion


        #region bounds settings
        ValueScaleType _upper_type = ValueScaleType.Unset;
        [Description("The type of the upper bounds value.")]
        public ValueScaleType UpperBoundryType { get { return _upper_type; } set { _upper_type = value; } }

        double _upper_bound = 0.0;
        [Description("The upper bounds value for this entity.")]
        public double UpperBoundsValue { get { return _upper_bound; } set { _upper_bound = value; } }

        ValueScaleType _lower_type = ValueScaleType.Unset;
        [Description("The type of the lower bounds value.")]
        public ValueScaleType LowerBoundryType { get { return _lower_type; } set { _lower_type = value; } }
        
        double _lower_bound = 0.0;
        [Description("The lower bounds value for this entity.")]
        public double LowerBoundsValue { get { return _lower_bound; } set { _lower_bound = value; } }

        public double GetUpperBoundsPrice(double order_price)
        {
            switch (_upper_type)
            {
                case ValueScaleType.Fixed:
                    return (order_price + _upper_bound);
                case ValueScaleType.Percentage:
                    return (order_price + ((_upper_bound / 100) * order_price));
                case ValueScaleType.Unset:
                default:
                    return order_price;
            }
        }
        public double GetLowerBoundsPrice(double order_price)
        {
            switch (_lower_type)
            {
                case ValueScaleType.Fixed:
                    return (order_price - _lower_bound);
                case ValueScaleType.Percentage:
                    return (order_price - ((_lower_bound / 100) * order_price));
                case ValueScaleType.Unset:
                default:
                    return order_price;
            }
        }
        #endregion
        #endregion
    }




    [Serializable]
    [TypeConverter(typeof(NamedConverter))]
    [Editor(typeof(ComparableCustomEditor<TradeEntities, TradeEntitiesForm>), typeof(UITypeEditor))]
    public class TradeEntities : XMLFileSerializeBase, ICustomComparable
    {
        public TradeEntities() { }
        public TradeEntities(string defact)
        { _default_account = defact; }
        public TradeEntities(string defact, double bspr)
        { _default_account = defact; _default_base_spread = bspr; }
        public TradeEntities(string defact, ValueScaleType ost, double os, double bspr)
        { _default_account = defact; _default_base_spread = bspr; _default_order_size = os; _default_order_size_type = ost; }
        public TradeEntities(string defact, ValueScaleType ubt, double ubv, ValueScaleType lbt, double lbv)
        { _default_account = defact; _default_upper_type = ubt; _default_upper_bound = ubv; _default_lower_type = lbt; _default_lower_bound = lbv; }
        public TradeEntities(string defact, ValueScaleType ost, double os, ValueScaleType ubt, double ubv, ValueScaleType lbt, double lbv)
        { _default_account = defact; _default_upper_type = ubt; _default_upper_bound = ubv; _default_lower_type = lbt; _default_lower_bound = lbv; _default_order_size = os; _default_order_size_type = ost; }
        public TradeEntities(string defact, ValueScaleType ubt, double ubv, ValueScaleType lbt, double lbv, double bspr)
        { _default_account = defact; _default_upper_type = ubt; _default_upper_bound = ubv; _default_lower_type = lbt; _default_lower_bound = lbv; _default_base_spread = bspr; }
        public TradeEntities(string defact, ValueScaleType ost, double os, ValueScaleType ubt, double ubv, ValueScaleType lbt, double lbv, double bspr)
        { _default_account = defact; _default_upper_type = ubt; _default_upper_bound = ubv; _default_lower_type = lbt; _default_lower_bound = lbv; _default_base_spread = bspr; _default_order_size = os; _default_order_size_type = ost; }

        public TradeEntities(TradeEntities src) { Copy(src); }
        public void Copy(TradeEntities src)
        {
            _default_account = src._default_account;
            _default_base_spread = src._default_base_spread;
            _default_lower_bound = src._default_lower_bound;
            _default_lower_type = src._default_lower_type;
            _default_order_size = src._default_order_size;
            _default_order_size_type = src._default_order_size_type;
            _default_upper_bound = src._default_upper_bound;
            _default_upper_type = src._default_upper_type;
            _refresh_on_lookup = src._refresh_on_lookup;

            _entities.Clear();
            foreach(string n in src._entities.Keys)
            {
                _entities.Add(n, src._entities[n]);
            }
        }
        public int CompareValues(ICustomComparable v)
        {
            TradeEntities src = (TradeEntities)v;
            int n = _default_account.CompareTo(src._default_account);
            if (n!=0){return n;}
            
            if (_default_base_spread > src._default_base_spread)
            { return 1; }
            else if (_default_base_spread < src._default_base_spread)
            { return -1; }

            if (_default_lower_bound > src._default_lower_bound)
            { return 1; }
            else if (_default_lower_bound < src._default_lower_bound)
            { return -1; }

            if (_default_lower_type > src._default_lower_type)
            { return 1; }
            else if (_default_lower_type < src._default_lower_type)
            { return -1; }

            if (_default_order_size > src._default_order_size)
            { return 1; }
            else if (_default_order_size < src._default_order_size)
            { return -1; }

            if (_default_order_size_type > src._default_order_size_type)
            { return 1; }
            else if (_default_order_size_type < src._default_order_size_type)
            { return -1; }

            if (_default_upper_bound > src._default_upper_bound)
            { return 1; }
            else if (_default_upper_bound < src._default_upper_bound)
            { return -1; }

            if (_default_upper_type > src._default_upper_type)
            { return 1; }
            else if (_default_upper_type < src._default_upper_type)
            { return -1; }

            if (_refresh_on_lookup && !src._refresh_on_lookup)
            { return 1; }
            else if ( !_refresh_on_lookup && src._refresh_on_lookup)
            { return -1; }

            if (_entities.Count > src._entities.Count)
            { return 1; }
            else if (_entities.Count < src._entities.Count)
            { return -1; }

            foreach (string name in src._entities.Keys)
            {
                if (!_entities.ContainsKey(name))
                { return -1; }
                
                n = _entities[name].CompareValues(src._entities[name]);
                if (n != 0) { return n; }
            }

            return 0;
        }

        private bool _refresh_on_lookup = false;
        [XmlIgnore,Browsable(false)]
        public bool RefreshOnLookup { get { return _refresh_on_lookup; } set { _refresh_on_lookup = value; } }

        #region default value members
        private string _default_account = string.Empty;
        [Description("If there's no matching entity, this account will be used.")]
        public string DefaultAccount { get { return _default_account; } set { _default_account = value; } }

        double _default_base_spread = 0.0;
        [Description("If there's no matching entity, this value will be used as the default spread.")]
        public double DefaultBaseSpread { get { return _default_base_spread; } set { _default_base_spread = value; } }

        double _default_order_size = 0.0;
        [Description("If there's no matching entity, this value will be used as the order size.")]
        public double DefaultOrderSize { get { return _default_order_size; } set { _default_order_size = value; } }

        ValueScaleType _default_order_size_type = ValueScaleType.Unset;
        [Description("If there's no matching entity, this value will be used as the order size type.")]
        public ValueScaleType DefaultOrderSizeType { get { return _default_order_size_type; } set { _default_order_size_type = value; } }


        #region bounds defaults
        ValueScaleType _default_upper_type = ValueScaleType.Unset;
        [Description("If there's no matching entity, this value will be used as the upper bounds value type.")]
        public ValueScaleType DefaultUpperBoundsType { get { return _default_upper_type; } set { _default_upper_type = value; } }
        
        double _default_upper_bound = 0.0;
        [Description("If there's no matching entity, this value will be used as the upper bounds value.")]
        public double DefaultUpperBoundsValue { get { return _default_upper_bound; } set { _default_upper_bound = value; } }

        ValueScaleType _default_lower_type = ValueScaleType.Unset;
        [Description("If there's no matching entity, this value will be used as the lower bounds value type.")]
        public ValueScaleType DefaultLowerBoundsType { get { return _default_lower_type; } set { _default_lower_type = value; } }
        
        double _default_lower_bound = 0.0;
        [Description("If there's no matching entity, this value will be used as the lower bounds value.")]
        public double DefaultLowerBoundsValue { get { return _default_lower_bound; } set { _default_lower_bound = value; } }
        #endregion
        #endregion

        private SerializableDictionary<string, TradeEntity> _entities = new SerializableDictionary<string, TradeEntity>();

        [Editor(typeof(CustomComparableDictionaryEditor<TradeEntity,CustomComparableDictionaryForm<TradeEntity>>), typeof(UITypeEditor))]
        [Description("A collection of Trade Entities which defines the specific trading values for a particular symbol and order direction.")]
        public SerializableDictionary<string, TradeEntity> Entities { get { return _entities; } set { _entities = value; } }

        public double GetDefaultUpperBoundsPrice(double order_price)
        {
            if (_refresh_on_lookup && !string.IsNullOrEmpty(FileName))
            {
                FunctionResult fres=refresh<TradeEntities>();
                if (fres.Error) { return -1.0; }
            }
            switch (_default_upper_type)
            {
                case ValueScaleType.Fixed:
                    return (order_price + _default_upper_bound);
                case ValueScaleType.Percentage:
                    return (order_price + ((_default_upper_bound / 100) * order_price));
                case ValueScaleType.Unset:
                default:
                    return order_price;
            }
        }
        public double GetDefaultLowerBoundsPrice(double order_price)
        {
            if (_refresh_on_lookup && !string.IsNullOrEmpty(FileName))
            {
                FunctionResult fres = refresh<TradeEntities>();
                if (fres.Error) { return -1.0; }
            }

            switch (_default_lower_type)
            {
                case ValueScaleType.Fixed:
                    return (order_price - _default_lower_bound);
                case ValueScaleType.Percentage:
                    return (order_price - ((_default_lower_bound / 100) * order_price));
                case ValueScaleType.Unset:
                default:
                    return order_price;
            }
        }

        #region entity element value access
        public double GetUpperBoundsPrice(string EntityID, double order_price)
        {
            if (_refresh_on_lookup && !string.IsNullOrEmpty(FileName))
            {
                FunctionResult fres = refresh<TradeEntities>();
                if (fres.Error) { return -1.0; }
            }
            
            TradeEntity te = null;
            if (!_entities.TryGetValue(EntityID, out te))
            {
                return GetDefaultUpperBoundsPrice(order_price);
            }
            return te.GetUpperBoundsPrice(order_price);
        }
        public double GetLowerBoundsPrice(string EntityID, double order_price)
        {
            if (_refresh_on_lookup && !string.IsNullOrEmpty(FileName))
            {
                FunctionResult fres = refresh<TradeEntities>();
                if (fres.Error) { return -1.0; }
            }

            TradeEntity te = null;
            if (!_entities.TryGetValue(EntityID, out te))
            {
                return GetDefaultLowerBoundsPrice(order_price);
            }
            return te.GetLowerBoundsPrice(order_price);
        }
        public string GetAccount(string EntityID)
        {
            if (_refresh_on_lookup && !string.IsNullOrEmpty(FileName))
            {
                FunctionResult fres = refresh<TradeEntities>();
                if (fres.Error) { return string.Empty; }
            }

            TradeEntity te = null;
            if (!_entities.TryGetValue(EntityID, out te))
            {
                return _default_account;
            }
            return te.Account;
        }
        public double GetBaseSpread(string EntityID)
        {
            if (_refresh_on_lookup && !string.IsNullOrEmpty(FileName))
            {
                FunctionResult fres = refresh<TradeEntities>();
                if (fres.Error) { return -1.0; }
            }

            TradeEntity te = null;
            if (!_entities.TryGetValue(EntityID, out  te))
            {
                return _default_base_spread;
            }
            return te.BaseSpread;
        }

        public double GetOrderSize(string EntityID)
        {
            if (_refresh_on_lookup && !string.IsNullOrEmpty(FileName))
            {
                FunctionResult fres = refresh<TradeEntities>();
                if (fres.Error) { return -1.0; }
            }

            TradeEntity te = null;
            if (!_entities.TryGetValue(EntityID, out  te))
            {
                return _default_order_size;
            }
            return te.OrderSize;
        }
        public ValueScaleType GetOrderSizeType(string EntityID)
        {
            if (_refresh_on_lookup && !string.IsNullOrEmpty(FileName))
            {
                FunctionResult fres = refresh<TradeEntities>();
                if (fres.Error) { return ValueScaleType.Unset; }
            }

            TradeEntity te = null;
            if (!_entities.TryGetValue(EntityID, out  te))
            {
                return _default_order_size_type;
            }
            return te.OrderSizeType;
        }

        public FunctionObjectResult<AccountValue> GetAccountValue(string EntityID, AccountValuesStore avs)
        {
            FunctionObjectResult<AccountValue> fres = new FunctionObjectResult<AccountValue>();

            if (_refresh_on_lookup && !string.IsNullOrEmpty(FileName))
            {
                FunctionResult res = refresh<TradeEntities>();
                if (res.Error) { fres.setError(res.Message);  return fres; }
            }
            
            TradeEntity te = null;
            if (!_entities.TryGetValue(EntityID, out  te))
            {
                if (!avs.Values.ContainsKey(_default_account))
                {
                    if (avs.Values.Count == 0)
                    {
                        fres.setError("AccountValuesStore is empty!");
                        return fres;
                    }

                    foreach (string key in avs.Values.Keys)
                    {
                        fres.ResultObject = avs.Values[key];
                        return fres;
                    }
                }
                fres.ResultObject = avs.Values[_default_account];
                return fres;
            }

            if (!avs.Values.ContainsKey(te.Account))
            {
                if (!string.IsNullOrEmpty(te.Account))
                {
                    fres.setError("AccountValuesStore does not contain explicit account '" + te.Account + "'");
                    return fres;
                }
                if (avs.Values.Count == 0)
                {
                    fres.setError("AccountValuesStore is empty!");
                    return fres;
                }
                foreach (string key in avs.Values.Keys)
                {
                    te.Account = key;
                    fres.ResultObject = avs.Values[key];
                    return fres;
                }
            }

            fres.ResultObject = avs.Values[te.Account];
            return fres;
        }

        public int GetOrderSizeValue(string EntityID, AccountValue av, double base_pr)
        {
            if (_refresh_on_lookup && !string.IsNullOrEmpty(FileName))
            {
                FunctionResult res = refresh<TradeEntities>();
                if (res.Error) { return -1; }
            }
            
            TradeEntity te = null;
            if (!_entities.TryGetValue(EntityID, out  te))
            {
                switch (_default_order_size_type)
                {
                    case ValueScaleType.Fixed:
                        return (int)Math.Floor(_default_order_size);

                    case ValueScaleType.Percentage:
                        return (int)Math.Floor(((av.MarginAvailable * av.MarginRate) / base_pr) * (_default_order_size / 100.0));

                    case ValueScaleType.Unset:
                    default:
                        return -1;
                }
            }
            return te.GetOrderSizeValue(av, base_pr);

        }
        #endregion

        public override FunctionResult clear()
        {
            _default_account = string.Empty;
            _default_base_spread = 0.0;
            _default_lower_bound = 0.0;
            _default_lower_type = ValueScaleType.Unset;
            _default_order_size = 0.0;
            _default_order_size_type = ValueScaleType.Unset;
            _default_upper_bound = 0.0;
            _default_upper_type = ValueScaleType.Unset;
            _entities.Clear();
            return base.clear();
        }
        public override FunctionResult initFromSerialized<T>(T src)
        {
            if (typeof(TradeEntities) == src.GetType())
            {
                TradeEntities tes = ((TradeEntities)((object)src));

                
                _default_account = tes._default_account;
                _default_base_spread = tes._default_base_spread;
                _default_lower_bound = tes._default_lower_bound;
                _default_lower_type = tes._default_lower_type;
                _default_order_size = tes._default_order_size;
                _default_order_size_type = tes._default_order_size_type;
                _default_upper_bound = tes._default_upper_bound;
                _default_upper_type = tes._default_upper_type;
                
                _entities = tes._entities;
            }

            return base.initFromSerialized<T>(src);
        }
        public override FunctionResult initFromSerialized(Type t, object src)
        {
            if (typeof(TradeEntities) == t)
            {
                TradeEntities tes = (TradeEntities)src;
                
                _default_account = tes._default_account;
                _default_base_spread = tes._default_base_spread;
                _default_lower_bound = tes._default_lower_bound;
                _default_lower_type = tes._default_lower_type;
                _default_order_size = tes._default_order_size;
                _default_order_size_type = tes._default_order_size_type;
                _default_upper_bound = tes._default_upper_bound;
                _default_upper_type = tes._default_upper_type;
                
                _entities = tes._entities;
            }

            return base.initFromSerialized(t, src);
        }
    }
    #endregion
}