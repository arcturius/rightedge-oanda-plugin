using System;
using System.IO;
using System.Xml.Serialization;
using System.ComponentModel;

using MATS.Common;
using RightEdge.Common;

namespace MATS
{
    namespace Systems
    {

        ////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////
        public class AccountValue
        {
            private string _account_id = string.Empty;
            public string AccountID { get { return _account_id; } }

            private double _margin_available = 0.0;
            public double MarginAvailable { get { return _margin_available; } }

            private double _buying_power = 0.0;
            public double BuyingPower { get { return _buying_power; } }
            //....
        }
        [Serializable]
        public class AccountValuesStore : XMLFileSerializeBase
        {
            public AccountValuesStore() { }

            MATS.Common.SerializableDictionary<string, AccountValue> _values = null;
            public MATS.Common.SerializableDictionary<string, AccountValue> Values { get { return _values; } }
        }
        ////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////





        //none : ignore bounds value
        //fixed : add a fixed amount to entry price for upper bounds (subtract for lower bounds value)
        //percentage : add a percentage of the entry price to find the upper bounds price (...)
        public enum BoundryType { None, Fixed, Percentage };

        [Serializable]
        public class TradeEntity : INamedObject
        {
            private TradeEntity() { }
            public TradeEntity(string n, string s, PositionType pt, OrderType ot)
            { _ename = n; _symbol = s; _pt = pt; _ot = ot; }
            public TradeEntity(string n, string s, PositionType pt, OrderType ot, string act)
            { _ename = n; _symbol = s; _pt = pt; _ot = ot; _account = act; }
            public TradeEntity(string n, string s, PositionType pt, OrderType ot, string act, double bspr)
            { _ename = n; _symbol = s; _pt = pt; _ot = ot; _account = act; _base_spread = bspr; }
            public TradeEntity(string n, string s, PositionType pt, OrderType ot, string act, BoundryType ubt, double ubv, BoundryType lbt, double lbv)
            { _ename = n; _symbol = s; _pt = pt; _ot = ot; _account = act; _upper_type = ubt; _upper_bound = ubv; _lower_type = lbt; _lower_bound = lbv; }
            public TradeEntity(string n, string s, PositionType pt, OrderType ot, string act, BoundryType ubt, double ubv, BoundryType lbt, double lbv, double bspr)
            { _ename = n; _symbol = s; _pt = pt; _ot = ot; _account = act; _upper_type = ubt; _upper_bound = ubv; _lower_type = lbt; _lower_bound = lbv; _base_spread = bspr; }

            #region key values
            private string _ename;
            public string EntityName { get { return _ename; } set { _ename = value; } }

            private string _symbol;
            public string SymbolName { get { return _symbol; } set { _symbol = value; } }

            private PositionType _pt;
            public PositionType PositionType { get { return _pt; } set { _pt = value; } }

            private OrderType _ot;
            public OrderType OrderType { get { return _ot; } set { _ot = value; } }

            [XmlIgnore]
            public string ID { get { return (_ename + ":" + _symbol + ":" + _pt.ToString() + ":" + _ot.ToString()); } }
            [XmlIgnore]
            public string Name { get { return ID; } }
            #endregion

            #region lookup values
            private string _account = string.Empty;
            public string Account { get { return _account; } set { _account = value; } }

            private double _base_spread = 0.0;
            public double BaseSpread { get { return _base_spread; } set { _base_spread = value; } }

            #region order sizing
            private double _order_size = 100.0;
            public double OrderSize { get { return _order_size; } set { _order_size = value; } }

            private OrderSizeType _order_size_type = OrderSizeType.Percentage;
            public OrderSizeType OrderSizeType { get { return _order_size_type; } set { _order_size_type = value; } }
            #endregion


            #region bounds settings
            BoundryType _upper_type = BoundryType.None;
            public BoundryType UpperBoundryType { get { return _upper_type; } set { _upper_type = value; } }
            double _upper_bound = 0.0;
            public double UpperBoundsValue { get { return _upper_bound; } set { _upper_bound = value; } }

            BoundryType _lower_type = BoundryType.None;
            public BoundryType LowerBoundryType { get { return _lower_type; } set { _lower_type = value; } }
            double _lower_bound = 0.0;
            public double LowerBoundsValue { get { return _lower_bound; } set { _lower_bound = value; } }

            public double GetUpperBoundsPrice(double order_price)
            {
                switch (_upper_type)
                {
                    case BoundryType.Fixed:
                        return (order_price + _upper_bound);
                    case BoundryType.Percentage:
                        return (order_price + ((_upper_bound / 100) * order_price));
                    case BoundryType.None:
                    default:
                        return order_price;
                }
            }
            public double GetLowerBoundsPrice(double order_price)
            {
                switch (_lower_type)
                {
                    case BoundryType.Fixed:
                        return (order_price - _lower_bound);
                    case BoundryType.Percentage:
                        return (order_price - ((_lower_bound / 100) * order_price));
                    case BoundryType.None:
                    default:
                        return order_price;
                }
            }
            #endregion
            #endregion
        }

        [Serializable]
        public class TradeEntities : XMLFileSerializeBase
        {
            public TradeEntities() { }
            public TradeEntities(string defact)
            { _default_account = defact; }
            public TradeEntities(string defact, double bspr)
            { _default_account = defact; _default_base_spread = bspr; }
            public TradeEntities(string defact, BoundryType ubt, double ubv, BoundryType lbt, double lbv)
            { _default_account = defact; _default_upper_type = ubt; _default_upper_bound = ubv; _default_lower_type = lbt; _default_lower_bound = lbv; }
            public TradeEntities(string defact, BoundryType ubt, double ubv, BoundryType lbt, double lbv, double bspr)
            { _default_account = defact; _default_upper_type = ubt; _default_upper_bound = ubv; _default_lower_type = lbt; _default_lower_bound = lbv; _default_base_spread = bspr; }

            #region default value members
            private string _default_account = string.Empty;
            [XmlIgnore]
            public string DefaultAccount { get { return _default_account; } set { _default_account = value; } }

            double _default_base_spread = 0.0;
            [XmlIgnore]
            public double DefaultBaseSpread { get { return _default_base_spread; } set { _default_base_spread = value; } }

            double _default_order_size = 0.0;
            [XmlIgnore]
            public double DefaultOrderSize { get { return _default_order_size; } set { _default_order_size = value; } }

            OrderSizeType _default_order_size_type;
            [XmlIgnore]
            public OrderSizeType DefaultOrderSizeType { get { return _default_order_size_type; } set { _default_order_size_type = value; } }


            #region bounds defaults
            BoundryType _default_upper_type = BoundryType.None;
            [XmlIgnore]
            public BoundryType DefaultUpperBoundsType { get { return _default_upper_type; } set { _default_upper_type = value; } }
            double _default_upper_bound = 0.0;
            [XmlIgnore]
            public double DefaultUpperBoundsValue { get { return _default_upper_bound; } set { _default_upper_bound = value; } }

            BoundryType _default_lower_type = BoundryType.None;
            [XmlIgnore]
            public BoundryType DefaultLowerBoundsType { get { return _default_lower_type; } set { _default_lower_type = value; } }
            double _default_lower_bound = 0.0;
            [XmlIgnore]
            public double DefaultLowerBoundsValue { get { return _default_lower_bound; } set { _default_lower_bound = value; } }
            #endregion
            #endregion

            private MATS.Common.SerializableDictionary<string, TradeEntity> _entities = new MATS.Common.SerializableDictionary<string, TradeEntity>();
            public MATS.Common.SerializableDictionary<string, TradeEntity> Entities { get { return _entities; } set { _entities = value; } }

            public double GetDefaultUpperBoundsPrice(double order_price)
            {
                switch (_default_upper_type)
                {
                    case BoundryType.Fixed:
                        return (order_price + _default_upper_bound);
                    case BoundryType.Percentage:
                        return (order_price + ((_default_upper_bound / 100) * order_price));
                    case BoundryType.None:
                    default:
                        return order_price;
                }
            }
            public double GetDefaultLowerBoundsPrice(double order_price)
            {
                switch (_default_lower_type)
                {
                    case BoundryType.Fixed:
                        return (order_price - _default_lower_bound);
                    case BoundryType.Percentage:
                        return (order_price - ((_default_lower_bound / 100) * order_price));
                    case BoundryType.None:
                    default:
                        return order_price;
                }
            }

            #region entity element value access
            //******************************
            public double GetUpperBoundsPrice(string EntityID, double order_price)
            {
                TradeEntity te = null;
                if (!_entities.TryGetValue(EntityID, out te))
                {
                    return GetDefaultUpperBoundsPrice(order_price);
                }
                return te.GetUpperBoundsPrice(order_price);
            }
            public double GetLowerBoundsPrice(string EntityID, double order_price)
            {
                TradeEntity te = null;
                if (!_entities.TryGetValue(EntityID, out te))
                {
                    return GetDefaultLowerBoundsPrice(order_price);
                }
                return te.GetLowerBoundsPrice(order_price);
            }
            public string GetAccount(string EntityID)
            {
                TradeEntity te = null;
                if (!_entities.TryGetValue(EntityID, out te))
                {
                    return _default_account;
                }
                return te.Account;
            }
            public double GetBaseSpread(string EntityID)
            {
                TradeEntity te = null;
                if (!_entities.TryGetValue(EntityID, out  te))
                {
                    return _default_base_spread;
                }
                return te.BaseSpread;
            }

            public double GetOrderSize(string EntityID)
            {
                TradeEntity te = null;
                if (!_entities.TryGetValue(EntityID, out  te))
                {
                    return _default_order_size;
                }
                return te.OrderSize;
            }
            public OrderSizeType GetOrderSizeType(string EntityID)
            {
                TradeEntity te = null;
                if (!_entities.TryGetValue(EntityID, out  te))
                {
                    return _default_order_size_type;
                }
                return te.OrderSizeType;
            }
            //******************************
            #endregion
        }
    }
}