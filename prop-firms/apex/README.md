# Apex Trader Funding — Reglas Operativas

## 1. Reglas de Evaluación
- **Tipo de Drawdown**: **Intradía Trailing** (se calcula en tiempo real con el pico máximo de ganancias flotantes / no realizadas).
- **Límite de Pérdida Diaria (DLL)**: No tiene límite formal diario, pero tocar el Trailing Drawdown elimina la cuenta inmediatamente.
- **Días mínimos para aprobar**: Desde 1 a 7 días según promociones vigentes (estándar: 7 días operados).
- **Horario límite**: Todas las posiciones y órdenes pendientes deben cerrarse antes de las **16:59 ET**. No se permite dejar posiciones abiertas *overnight*.

## 2. Reglas en Cuenta Fondeada (PA - Performance Account)
- **Freno del Trailing Drawdown**: En cuentas PA el *Trailing Threshold* deja de subir cuando el balance alcanza el `Tamaño Inicial + Max Drawdown + $100` (ej. en cuenta de 50k se congela en **$50,100** al llegar a $52,600).
- **Escalado de contratos (Scaling)**: En PA solo puedes operar la **mitad** de los contratos máximos hasta superar el umbral del colchón (*Trailing Threshold Stop*).
- **Regla del 30% de Consistencia (Retiros)**: Ningún día de trading puede representar más del **30%** del beneficio total acumulado desde el último retiro aprobado.
- **Regla de Pérdida Flotante (30% MAE - Negative P&L)**: Ninguna operación abierta debe exceder un drawdown flotante mayor al **30%** del beneficio acumulado al inicio del día (o 30% del umbral inicial si no hay colchón; hasta 50% si duplicas el colchón de seguridad).
- **Relación Riesgo/Beneficio (5:1)**: No se permiten stops más de 5 veces mayores que el objetivo de beneficio (ej. arriesgar 50 ticks para ganar 10 ticks).

## 3. Planes principales (Rithmic / Tradovate)

| Plan | Balance | Profit Target | Max Trailing Drawdown | Contratos (Eval) | Contratos Iniciales (PA) | Colchón PA (Safety Net) |
| :--- | :---: | :---: | :---: | :---: | :---: | :---: |
| **25k Full** | $25,000 | $1,500 | $1,500 | 4 Minis (40 Micros) | 2 Minis | $26,600 |
| **50k Full** | $50,000 | $3,000 | $2,500 | 10 Minis (100 Micros) | 5 Minis | $52,600 |
| **100k Full** | $100,000 | $6,000 | $3,000 | 14 Minis (140 Micros) | 7 Minis | $103,100 |
| **150k Full** | $150,000 | $9,000 | $5,000 | 17 Minis (170 Micros) | 8 Minis | $155,100 |
| **250k Full** | $250,000 | $15,000 | $6,500 | 27 Minis (270 Micros) | 13 Minis | $256,600 |
| **300k Full** | $300,000 | $20,000 | $7,500 | 35 Minis (350 Micros) | 17 Minis | $307,600 |
