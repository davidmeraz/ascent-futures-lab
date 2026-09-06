# Plataformas: NinjaTrader 8 y TradingView

Este directorio contiene las estrategias e indicadores listos para usar en cada plataforma, manteniendo las mismas reglas operativas y de gestión de riesgo para cuentas de fondeo.

## Estructura

- **`ninjatrader/`** (NinjaScript / C#)
  - [`Indicators/dav3e_hud.cs`](./ninjatrader/Indicators/dav3e_hud.cs): Panel operativo (HUD) con delta Bid/Ask, ratio delta/volumen, ATR Wilder y ticks por minuto.
  - [`Indicators/dav3e_vwap.cs`](./ninjatrader/Indicators/dav3e_vwap.cs): VWAP anclado por hora de sesión con bandas sigma, pendiente y marca de extensión.
  - [`Strategies/SmaCrossoverAdapter.cs`](./ninjatrader/Strategies/SmaCrossoverAdapter.cs): Estrategia base de cruce de medias móviles para NinjaTrader 8.
- **`tradingview/`** (Pine Script v5)
  - [`indicators/dav3e_vwap.pine`](./tradingview/indicators/dav3e_vwap.pine): Equivalente en Pine Script del VWAP anclado con bandas sigma, distancia extendida y tabla HUD.
  - [`strategies/sma_crossover.pine`](./tradingview/strategies/sma_crossover.pine): Estrategia de cruce de medias con control de límites de cuenta de fondeo (pérdida máxima diaria, trailing drawdown y cierre de sesión).
