# Plan del Proyecto

## Objetivo

Analizar reglas de **empresas de fondeo de futuros (NQ / MNQ / ES / MES)** y diseñar estrategias e indicadores consistentes para **NinjaTrader 8** (NinjaScript C#) y **TradingView** (Pine Script).

---

## Módulos principales

| Módulo | Ubicación | Propósito |
| :--- | :--- | :--- |
| **1. Reglas de Empresas de Fondeo** | [`prop-firms/`](../prop-firms/README.md) | Documentar y estructurar en JSON los planes, tipos de drawdown (Intradía vs EOD), límites diarios (DLL), reglas de consistencia y condiciones de retiro de cada empresa. |
| **2. Scripts para Plataformas** | [`platforms/`](../platforms/README.md) | Mantener indicadores y estrategias equivalentes para **NinjaTrader 8** (`.cs`) y **TradingView** (`.pine`) con controles de riesgo adaptados a cuentas de fondeo. |
| **3. Motor de Backtesting** | [`ascent-backtester/`](../ascent-backtester/README.md) | Repositorio Git independiente con el motor en C# (`core`, `analyzer`, `app`) para evaluar estrategias sobre históricos y medir cumplimiento de reglas de fondeo. |
