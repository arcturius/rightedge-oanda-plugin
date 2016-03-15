# Introduction #

Ultimately, the goal of this project has been to expose the full realm of
Oanda's trade execution capabilities to the RightEdge environment. This update
completes the full feature set implementation and we're now ready for some
rigorous testing.

The RightEdge IBroker interface working with the Position Manager object is
inherently tied to a single account. Therefore, to support multiple trading
accounts through a single running system an alternative had to be constructed.
The end result was two data files, Trade Entities and Account Values, which
can be kept in sync between the Oanda plugin and the running trading
system.

## Limitation : no "order-counters-order" trading ##

There is one remaining limitation that can not be worked around. Oanda only
supports trading one direction at a time on a symbol in a given account. In
other words, you can't be both long and short on the same pair in the same
account at the same time. As a result of this policy when an open order is
traded against with a countering limit or market order, the original open
order is modified and/or closed. Because of the way order objects are
structured responding to these events would require the plugin to initiate a
"new" RightEdge close order to terminate the original open order (and possibly
the opposing order as well) Currently, RightEdge does not allow the broker
plugin to initiate orders from the plugin into RightEdge space. The plugin can
only respond to the input it receives from RightEdge and communicate the results
of that input from Oanda.

# Communicating with a Trading System #
## Trade Entities ##
### For the Broker ###

The Trade Entities data file contains the trading directives for a
particular symbol and order direction (ie long or short). Included in the
directives are the account to use, and the bounds values to set on any orders
the broker receives.

There is an easy way to edit this file in the plugin system options. Simply bring up the RightEdge "Other Settings" service options for your configuration of the plugin and select the "Edit Trade Entities" button in the toolbar. This will open up the file you have specified in the plugin's options.

### For the Trading System ###
There is also a data element in the Trade Entity to direct a trading system's allocation. A Trading system can thus use the Order Size and Order Size Type values in combination with the Account and Account Values data to derive the appropriate trade size.

<-- some code examples......

## Account Values ##
### For the Broker ###
The Account Values file is used by the plugin to report the various account specific capital values (margin used, balance, etc..). If the Account Values File Name is specified then the plugin will write out the values for all accounts to the data file when GetMargin() and GetBuyingPower() are called by RightEdge. If the file is not specified, then the default account values are returned.

  * GetMargin() will update the MarginAvailable, MarginUsed, and MarginRate.
  * GetBuyingPower() will update the Balance.

### For the Trading System ###
  * how to read account values to get the account specific capital value...
  * how to combine with trade entities...

<-- some code examples......