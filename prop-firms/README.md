# Reglas de Empresas de Fondeo (Prop Firms)

Este directorio centraliza las reglas operativas, límites de riesgo y condiciones de retiro de las empresas de fondeo de futuros (NQ / MNQ / ES / MES).

Cada subcarpeta contiene:
1. **`README.md`**: Ficha legible con las reglas clave de evaluación, cuenta fondeada (PA / Funded), restricciones operativas y retiros.
2. **`plans.json`**: Definición estructurada siguiendo [`rules.schema.json`](./rules.schema.json) para que el analizador simule si una estrategia supera la prueba o viola algún límite.

---

## Comparativa rápida (Cuentas de 50k como referencia)

| Empresa | Objetivo (50k) | Max Drawdown | Tipo de Drawdown | Pérdida Diaria (DLL) | Consistencia (Retiro) | Cierre Obligatorio (ET) |
| :--- | :---: | :---: | :---: | :---: | :---: | :---: |
| **[Apex](./apex/README.md)** | $3,000 | $2,500 | **Intradía Trailing** (con flotante) | Sin límite formal | 30% en PA | 16:59 ET |
| **[Topstep](./topstep/README.md)** | $3,000 | $2,000 | **EOD** (al cierre del día) | $1,000 (Soft/Eliminación según versión) | 50% en Combine | 16:10 ET (15:10 CT) |
| **[MyFundedFutures](./my-funded-futures/README.md)** | $3,000 | $2,000–$2,500 | **EOD** | $1,200 (Starter) / Sin DLL (Expert) | 40% (Starter) / 0% (Expert) | 16:59 ET |
| **[Take Profit Trader](./take-profit-trader/README.md)** | $3,000 | $2,000 | **EOD** (Eval) / **Intradía** (PRO) | $1,100 | 50% en Evaluación | 17:00 ET |

---

## Conceptos clave para diseñar estrategias

1. **Drawdown Intradía Trailing vs. EOD (End of Day)**:
   - **Intradía Trailing (ej. Apex)**: El umbral de liquidación sube con el máximo beneficio flotante (*unrealized PnL*) alcanzado durante la operación. Si una operación sube +$1,000 y se devuelve a *break-even*, el colchón habrá perdido $1,000. Requiere **toma de ganancias definida (TP)** y evitar dejar correr grandes ganancias sin asegurar.
   - **EOD Trailing (ej. Topstep, MFFU)**: El umbral solo se actualiza al finalizar la sesión con el balance cerrado. Permite dejar correr ganadores intradía sin penalizar el colchón por retrocesos de ganancias flotantes.
2. **Límite de Pérdida Diaria (DLL - Daily Loss Limit)**:
   - Determina el riesgo máximo por operación (`Riesgo por Trade <= DLL / Máx. rachas perdedoras seguidas en un día`).
3. **Regla de Consistencia (30% - 50%)**:
   - Ningún día individual puede representar más del X% del beneficio total acumulado al solicitar retiro o aprobar la prueba.
4. **Colchón de Seguridad (*Safety Net / Buffer*)**:
   - Monto que debe acumularse en cuenta fondeada antes de que el trailing drawdown deje de subir (generalmente igual al `MaxDrawdown + $100`).
