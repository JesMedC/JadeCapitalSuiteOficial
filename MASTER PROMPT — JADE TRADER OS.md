# MASTER PROMPT — JADE TRADER OS

Actúa como un Principal Software Architect, Senior Full-Stack Engineer, Quant Developer, Product Designer UI/UX y especialista en plataformas financieras.

Tu misión es diseñar e implementar progresivamente una plataforma SaaS profesional llamada provisionalmente **Jade Trader OS**.

No construyas una aplicación genérica de “trading journal”.

El producto debe convertirse en el **centro operativo y analítico principal de un trader**, permitiéndole concentrar en una sola plataforma:

- cuentas;
- operaciones;
- balances;
- P&L;
- riesgo;
- journal;
- disciplina;
- estrategias;
- setups;
- estadísticas;
- análisis de performance;
- calendarios de resultados;
- alertas;
- detección de patrones;
- patrones armónicos;
- planificación de operaciones;
- inteligencia artificial;
- importaciones;
- integraciones futuras.

La plataforma debe soportar desde su diseño:

- Forex;
- Opciones binarias / digitales;
- Criptomonedas;
- Acciones;
- Índices;
- Futuros;
- Commodities;
- CFDs;
- Prop Firms.

## 1. PRINCIPIO FUNDAMENTAL

La V1 **NO ejecutará operaciones en brokers**.

El trader seguirá ejecutando externamente en:

- MT4;
- MT5;
- brokers;
- exchanges;
- plataformas de binarias;
- prop firms.

Jade Trader OS será inicialmente:

> ANALIZAR → PLANIFICAR → CONTROLAR RIESGO → REGISTRAR → EVALUAR → APRENDER

Posteriormente debe quedar preparada arquitectónicamente para incorporar sincronización y eventualmente ejecución, pero ninguna decisión de V1 debe depender de tener ejecución directa.

---

# 2. FILOSOFÍA DE PRODUCTO

Respeta siempre estas reglas.

## Regla 1 — Un módulo = una responsabilidad

No construir una aplicación donde todo aparezca dentro del Dashboard.

Ejemplos:

- Accounts es un módulo.
- Operations es otro.
- Journal es otro.
- Analytics es otro.
- Risk Center es otro.
- Strategies es otro.
- Scanner es otro.

El Dashboard solo resume.

## Regla 2 — No abusar de cards

Evitar interfaces con 20 tarjetas pequeñas.

Usar:

- tablas profesionales;
- paneles;
- gráficos grandes;
- secciones;
- tabs;
- side panels;
- drawers;
- filtros;
- drill-down.

## Regla 3 — Cada mercado tiene su propia lógica

Una opción binaria NO debe tratarse como una operación Forex.

Forex puede tener:

- entry;
- exit;
- stop loss;
- take profit;
- lot;
- commission;
- swap;
- R multiple.

Binarias puede tener:

- CALL/PUT;
- strike/entry;
- expiration;
- stake;
- payout;
- WIN/LOSS/DRAW.

Crear un modelo de dominio capaz de representar estas diferencias correctamente.

## Regla 4 — Los datos son el núcleo

No construir primero gráficos decorativos.

Primero:

Accounts → Trades → Balance → Risk → Journal → Analytics.

Las interfaces deben consumir datos reales persistidos.

## Regla 5 — Todo debe ser auditable

Las acciones importantes deben registrar:

- user;
- timestamp;
- previous value;
- new value;
- entity;
- action.

## Regla 6 — Diseñar para escalar

Aunque inicialmente se ejecute localmente, la arquitectura debe estar preparada para evolucionar a SaaS multiusuario.

---

# 3. STACK BASE

Usar como arquitectura inicial:

Backend:

- ASP.NET Core Web API;
- C#;
- Entity Framework Core;
- PostgreSQL;
- arquitectura modular;
- REST API;
- OpenAPI / Swagger;
- autenticación JWT + refresh tokens;
- ASP.NET Identity o arquitectura equivalente;
- SignalR para eventos/alertas en tiempo real cuando corresponda.

Frontend:

- React;
- TypeScript;
- Vite;
- componentes reutilizables;
- sistema de diseño centralizado;
- Tailwind CSS;
- biblioteca de componentes profesional compatible con React;
- React Query/TanStack Query para server state;
- React Router;
- formularios tipados y validados.

Infraestructura:

- Docker;
- Docker Compose;
- PostgreSQL;
- backend;
- frontend;
- Redis solo cuando exista una necesidad real;
- almacenamiento local compatible con migración posterior a S3/Blob Storage.

La primera versión debe iniciar localmente con:

```bash
docker compose up -d
```

No introducir Kubernetes ni microservicios prematuramente.

Utilizar inicialmente un **modular monolith** con límites claros entre dominios.

---

# 4. ARQUITECTURA

Organizar backend aproximadamente en:

```text
src/

BuildingBlocks/

Modules/
    Identity/
    Accounts/
    Trades/
    Portfolio/
    Journal/
    Risk/
    Analytics/
    Strategies/
    Planner/
    MarketData/
    Scanner/
    Alerts/
    Calendar/
    AI/
    Integrations/
    Subscriptions/
    Administration/

API/
```

Cada módulo debe tener sus propias:

- Domain;
- Application;
- Infrastructure;
- Contracts.

Evitar dependencias circulares.

Usar eventos de dominio/integración para comunicación cuando sea apropiado.

Ejemplo:

```text
TradeCreated
    ↓
UpdateAccountMetrics
    ↓
UpdateRiskMetrics
    ↓
UpdateAnalytics
    ↓
EvaluateAlerts
```

---

# 5. MULTITENANCY

Preparar desde el inicio:

```text
User
Workspace
WorkspaceMember
Role
Subscription
```

La mayoría de entidades de negocio deben tener:

```text
workspace_id
```

Nunca permitir acceso a datos de otro workspace.

Aplicar filtros globales/seguridad a nivel de aplicación.

---

# 6. ENTIDADES BASE

Implementar como mínimo:

```text
User
Workspace
WorkspaceMember

Account
Broker
AccountBalanceSnapshot
AccountTransaction

Trade
ForexTradeDetails
BinaryTradeDetails
CryptoTradeDetails
TradeExecution
TradeAttachment
TradeTag

Instrument
Market
Broker

Strategy
StrategyVersion
Setup
StrategyRule

TradePlan
TradePlanChecklist

JournalEntry
TradingSession
EmotionTag
DisciplineChecklist

RiskProfile
RiskRule
RiskSnapshot
RiskViolation

Alert
AlertRule

HarmonicPattern
PatternPoint
PatternDetection
PatternConfluence

EconomicEvent

ImportJob
Integration

AuditLog
```

Todas las entidades principales deben utilizar UUID.

Agregar:

```text
created_at
created_by
updated_at
updated_by
deleted_at
```

cuando corresponda.

Implementar soft delete donde tenga sentido.

---

# 7. NAVEGACIÓN PRINCIPAL

Desktop sidebar:

```text
Dashboard

Operations
Accounts
Journal
Analytics
P&L Calendar
Strategies
Pattern Scanner
Risk Center
Trade Planner

Markets
Alerts

AI Copilot

Settings
```

Top bar:

```text
Global Search
Quick Filters
+ Registrar operación
Alertas
AI Copilot
Perfil
```

El menú debe poder contraerse.

---

# 8. DISEÑO VISUAL

Crear una interfaz premium de trading.

Tema principal:

```text
Background       #080D12
Surface          #0D141B
Surface Elevated #111B24
Border           #1C2A35

Primary Jade     #2EDC8C
Primary Dark     #169F68

Profit           #35D07F
Loss             #FF5C5C
Warning          #F3B94E
Info             #4DA3FF

Text Primary     #E9F1F7
Text Secondary   #8FA1B2
Text Muted       #607080
```

Diseño:

- oscuro;
- elegante;
- fintech;
- profesional;
- denso pero legible;
- bordes sutiles;
- radius moderado;
- animaciones mínimas;
- evitar glow excesivo;
- priorizar información.

Tipografía sans-serif profesional.

Crear design tokens para:

- colors;
- typography;
- spacing;
- radius;
- shadows;
- status colors.

Debe existir soporte futuro para light mode.

---

# 9. MÓDULO 01 — ACCOUNTS

Debe ser el primer módulo funcional completo.

## Accounts overview

Ruta:

```text
/accounts
```

Cabecera:

```text
Cuentas

Patrimonio total
Capital depositado
P&L acumulado
Cuentas activas

+ Nueva cuenta
```

Filtros:

```text
Todas
Real
Demo
Prop Firm
Binarias
Crypto
Archivadas
```

Tabla:

```text
Cuenta
Broker
Mercado
Tipo
Moneda
Balance
Equity
P&L
P&L %
Drawdown
Estado
```

## Tipos de cuenta

Soportar:

```text
BROKER
PROP_FIRM
BINARY
CRYPTO_EXCHANGE
MANUAL
DEMO
```

## Detail

Ruta:

```text
/accounts/{id}
```

Tabs:

```text
Resumen
Operaciones
Balance
Riesgo
Estadísticas
Movimientos
Configuración
```

Registrar:

- balance inicial;
- depósitos;
- retiros;
- fees;
- ajustes.

Mantener historial mediante:

```text
AccountBalanceSnapshot
```

---

# 10. MÓDULO 02 — OPERATIONS

Ruta:

```text
/operations
```

Debe convertirse en el repositorio principal de operaciones.

Filtros:

```text
Fecha
Cuenta
Mercado
Instrumento
Dirección
Estrategia
Setup
Resultado
Session
Tag
Planificada/no planificada
```

Tabla:

```text
Fecha
Instrumento
Mercado
Cuenta
Dirección
Estrategia
Setup
Resultado
R
P&L
Estado
```

## Forex trade

Campos:

```text
instrument
account
direction LONG/SHORT
entry
exit
stop_loss
take_profit
position_size
lot_size
risk_amount
risk_percent
commission
swap
fees
gross_pnl
net_pnl
r_multiple
opened_at
closed_at
```

## Binary trade

Campos:

```text
instrument
account
direction CALL/PUT
entry_price
stake
payout_percent
expiration_seconds
opened_at
expired_at
result WIN/LOSS/DRAW
gross_pnl
net_pnl
```

Resultado esperado:

WIN:

```text
profit = stake × payout
```

LOSS:

```text
profit = -stake
```

DRAW:

configurable según broker.

Nunca aplicar fórmulas Forex a binarias.

---

# 11. MÓDULO 03 — DASHBOARD

Ruta:

```text
/dashboard
```

No saturar.

Mostrar solo información ejecutiva.

KPIs:

```text
Equity
Net P&L
P&L %
Win Rate
Expectancy
Profit Factor
Current Drawdown
```

Permitir selector:

```text
Todas las cuentas
Cuenta específica
```

y período:

```text
Hoy
7D
30D
Mes
Año
Personalizado
```

Gráfico principal:

```text
Equity Curve / Cumulative P&L
```

Secciones inferiores:

```text
Performance por mercado
Risk Status
Últimas operaciones
P&L Calendar resumido
Insights
Upcoming Alerts
```

---

# 12. MÓDULO 04 — P&L CALENDAR

Ruta:

```text
/calendar
```

Este módulo es prioritario.

Vistas:

```text
Day
Week
Month
Year
Heatmap
```

El calendario mensual debe mostrar en cada día:

```text
P&L
P&L %
R
Nº trades
```

Ejemplo:

```text
10 AGO

+$125.40
+1.82R
3 trades
```

Color:

- verde = positivo;
- rojo = negativo;
- neutral = break even;
- intensidad relacionada al rendimiento.

Nunca depender únicamente de color; incluir valores.

## Drawer del día

Al seleccionar fecha:

```text
P&L
R
Win Rate
Profit Factor
Trades
Risk Used
Drawdown intradía
Discipline Score
```

Mostrar las operaciones de ese día.

## Selector de métrica

Permitir visualizar el calendario como:

```text
P&L
P&L %
R
Win Rate
Trades
Drawdown
Disciplina
```

## Vista anual

Mostrar rendimiento mensual.

## Heatmap

Crear heatmap:

```text
weekday × hour
```

y adicionalmente:

```text
calendar-year heatmap
```

---

# 13. MÓDULO 05 — JOURNAL

Ruta:

```text
/journal
```

No construir solamente notas.

Modelo:

```text
PRE-TRADE
IN-TRADE
POST-TRADE
```

## Pre Trade

Registrar:

```text
trade thesis
strategy
setup
market condition
confidence
emotion
checklist
screenshots
```

## Post Trade

Registrar:

```text
seguí el plan?
quality score
repetiría la operación?
errores
aciertos
lección
estado emocional posterior
```

Clasificación:

```text
GOOD_TRADE_GOOD_RESULT
GOOD_TRADE_BAD_RESULT
BAD_TRADE_GOOD_RESULT
BAD_TRADE_BAD_RESULT
```

Esto debe ser independiente del resultado económico.

Crear:

```text
DisciplineScore
```

0–10.

---

# 14. MÓDULO 06 — RISK CENTER

Ruta:

```text
/risk
```

Configuraciones:

```text
maximum_risk_per_trade
maximum_daily_loss
maximum_weekly_loss
maximum_monthly_loss
maximum_drawdown
maximum_trades_per_day
maximum_consecutive_losses
maximum_open_risk
```

Estados:

```text
NORMAL
WARNING
HIGH_RISK
STOP_TRADING
```

Dashboard de riesgo:

```text
Daily Loss
Weekly Loss
Monthly Loss
Current DD
Maximum DD
Open Risk
Consecutive Losses
Trades Today
```

Crear `RiskViolation`.

Aunque Jade Trader OS no pueda bloquear físicamente el broker:

mostrar prominentemente:

```text
STOP TRADING
```

si una regla es violada.

---

# 15. MÓDULO 07 — ANALYTICS

Ruta:

```text
/analytics
```

Tabs:

```text
Overview
Performance
Strategies
Markets
Time
Risk
Behavior
Accounts
Binary
```

Calcular:

## Performance

```text
Net P&L
Gross Profit
Gross Loss
Win Rate
Loss Rate
Break Even Rate
Average Win
Average Loss
Largest Win
Largest Loss
Payoff Ratio
Profit Factor
Expectancy
Expectancy R
Average R
Total R
```

## Risk

```text
Maximum Drawdown
Average Drawdown
Recovery Factor
Average Risk
Maximum Risk
Risk of Ruin
Consecutive Wins
Consecutive Losses
```

## Execution

Cuando exista información suficiente:

```text
MAE
MFE
Entry Efficiency
Exit Efficiency
```

## Consistency

```text
Profitable Days %
Profitable Weeks %
Profitable Months %

Average Daily P&L
Average Weekly P&L
Average Monthly P&L

Best Day
Worst Day
Best Week
Worst Week
Best Month
Worst Month
```

## Behavioral

```text
Trades/day
Overtrading
Trades outside plan
Trades without checklist
Performance after losses
Performance after wins
Performance after 2/3 consecutive losses
```

## Temporal

Analizar por:

```text
hour
weekday
session
month
timeframe
trade duration
```

## Cross analytics

El backend debe ser capaz de analizar combinaciones:

```text
Strategy × Hour
Strategy × Instrument
Setup × Instrument
Setup × Session
Instrument × Session
Account × Strategy
Timeframe × Direction
Discipline × Performance
```

---

# 16. MÓDULO 08 — STRATEGIES / PLAYBOOK

Ruta:

```text
/strategies
```

Entidad:

```text
Strategy
```

Debe contener:

```text
name
description
market
status
current_version
rules
setups
```

Estados:

```text
DRAFT
BACKTEST
FORWARD_TEST
LIVE
PAUSED
ARCHIVED
```

Permitir versiones:

```text
Trend Pullback v1.0
Trend Pullback v1.1
Trend Pullback v2.0
```

No mezclar automáticamente performance de versiones diferentes.

Detalle:

```text
Overview
Rules
Setups
Trades
Performance
Versions
```

Setup debe ser entidad independiente.

---

# 17. MÓDULO 09 — PATTERN SCANNER

Ruta:

```text
/scanner
```

El scanner no ejecuta operaciones.

Su función es:

```text
DETECTAR
CLASIFICAR
MONITOREAR
ALERTAR
```

## Tabs iniciales

```text
All
Harmonics
Structure
Fibonacci
```

V1 concentrada principalmente en:

```text
Harmonics
```

## Patrones

Soportar:

```text
Gartley
Bat
Butterfly
Crab
Deep Crab
AB=CD
Cypher
```

Preparar arquitectura para:

```text
Shark
5-0
Three Drives
```

## Pattern engine

Implementar pivots usando algoritmo configurable tipo:

```text
ZigZag / Pivot detection
```

Parámetros configurables:

```text
depth
deviation
backstep
minimum_move
```

No acoplar la detección de patrones directamente a UI.

Crear servicio:

```text
IPatternDetectionEngine
```

y detector:

```text
IHarmonicPatternDetector
```

---

# 18. RATIOS ARMÓNICOS

Los ratios deben vivir en configuración, NO hardcodeados dentro de algoritmos.

Ejemplo de configuración inicial:

```text
GARTLEY

B/XA       ≈ 0.618
C/AB       0.382 – 0.886
D/XA       ≈ 0.786
CD/BC      1.272 – 1.618
```

```text
BAT

B/XA       0.382 – 0.500
C/AB       0.382 – 0.886
D/XA       ≈ 0.886
CD/BC      1.618 – 2.618
```

```text
BUTTERFLY

B/XA       ≈ 0.786
C/AB       0.382 – 0.886
D/XA       1.270 – 1.618
CD/BC      1.618 – 2.240
```

```text
CRAB

B/XA       0.382 – 0.618
C/AB       0.382 – 0.886
D/XA       ≈ 1.618
CD/BC      2.240 – 3.618
```

Permitir tolerancia configurable:

```text
ratio_tolerance_percent
```

Ejemplo:

```text
3%
5%
```

No declarar un patrón válido únicamente porque un ratio coincida.

Debe cumplirse una combinación definida de reglas.

---

# 19. MODELO DEL PATRÓN

Persistir:

```text
pattern_type
direction
symbol
timeframe

X_price
X_time

A_price
A_time

B_price
B_time

C_price
C_time

D_price
D_time

ratio_ab_xa
ratio_bc_ab
ratio_cd_bc
ratio_xd_xa

prz_start
prz_end

invalidation_price

target_1
target_2
target_3

detected_at
status
pattern_score
confluence_score
```

Estados:

```text
DETECTED
FORMING
APPROACHING_PRZ
PRZ_REACHED
CONFIRMATION
ACTIVE
COMPLETED
INVALIDATED
EXPIRED
```

Implementar state machine.

---

# 20. PATTERN SCORE

Crear un score 0–100.

NO llamar a este score:

```text
probabilidad de éxito
```

Debe representar:

```text
calidad geométrica + ratios + confluencias
```

Ejemplo:

```text
Pattern Geometry       40
Fibonacci Accuracy     25
PRZ Quality            15
Market Confluences     20
```

Total:

```text
100
```

El score debe ser explicable.

UI:

```text
Pattern Score 91/100

Geometry       38/40
Ratios         23/25
PRZ            14/15
Confluence     16/20
```

---

# 21. CONFLUENCIAS

Preparar motor para:

```text
Support / Resistance
EMA
RSI
Market Structure
Fibonacci
Volume
Session
Higher timeframe trend
```

Cada confluencia debe indicar:

```text
type
value
weight
passed
reason
```

---

# 22. HARMONIC DETAIL VIEW

Ruta:

```text
/scanner/{patternId}
```

Mostrar:

```text
EUR/USD
Bullish Gartley
M5

Pattern Score
91

Confluence
86

Estado
PRZ REACHED
```

Gráfico grande mostrando:

```text
X
A
B
C
D
PRZ
Invalidation
Targets
```

Panel ratios:

```text
B/XA
C/AB
CD/BC
D/XA
```

Mostrar:

```text
Expected
Actual
Deviation
Status
```

Ejemplo:

```text
B/XA

Expected 0.618
Actual   0.624
Deviation +0.97%
VALID
```

---

# 23. ALERT CENTER

Ruta:

```text
/alerts
```

Categorías:

```text
PATTERN
RISK
STRATEGY
MARKET
ACCOUNT
SYSTEM
```

Estados:

```text
UNREAD
READ
DISMISSED
ACTIONED
```

Ejemplos:

```text
EUR/USD

Bullish Gartley M5

Precio entrando en PRZ
Pattern Score 91
Confluence 86

[Analizar]
```

Risk:

```text
Daily risk

Has utilizado 82%
de tu límite diario.

[Ver Risk Center]
```

Crear preferencias:

```text
notify_pattern_detected
notify_approaching_prz
notify_prz_reached
notify_confirmation
notify_invalidated
```

Preparar posteriormente:

- email;
- push;
- Telegram;
- Slack;
- mobile.

V1:

```text
in-app
```

---

# 24. TRADE PLANNER

Ruta:

```text
/planner
```

El trader puede crear un plan manualmente o desde Scanner.

Modelo:

```text
instrument
account
strategy
setup
direction
entry_zone
entry
stop
target1
target2
target3
risk_percent
risk_amount
expected_rr
notes
pattern_id
```

Checklist:

```text
Trend valid
Setup valid
Entry zone
Confirmation
Risk valid
News checked
```

Estados:

```text
DRAFT
READY
EXECUTED
CANCELLED
EXPIRED
```

Al seleccionar:

```text
Marcar como ejecutado
```

permitir crear una operación utilizando los datos del plan.

Nunca generar la operación automáticamente sin acción explícita.

---

# 25. FLUJO SCANNER → TRADE

Implementar:

```text
PatternDetection
        ↓
Pattern Alert
        ↓
Pattern Detail
        ↓
Create Trade Plan
        ↓
Risk Validation
        ↓
Trader executes externally
        ↓
Register Trade
        ↓
Journal
        ↓
Analytics
```

Mantener relación:

```text
Trade
trade_plan_id
pattern_detection_id
strategy_id
setup_id
```

Esto permitirá medir performance real del scanner.

---

# 26. ANALYTICS DE PATRONES

Crear vista:

```text
Analytics → Patterns
```

Métricas:

```text
Pattern
Trades
Win Rate
Net P&L
R
Expectancy
Profit Factor
Average Risk
```

Permitir filtros:

```text
Pattern
Instrument
Timeframe
Direction
Session
Strategy
Account
```

Ejemplo:

```text
Bullish Gartley
EUR/USD
M5
London

21 Trades
76.2% WR
+19.4R
+0.92R Expectancy
```

Esto representa performance histórica DEL USUARIO.

No utilizar esos datos para afirmar que un futuro patrón tiene igual probabilidad.

---

# 27. BINARY ANALYTICS

Crear estadísticas específicas.

No usar R:R tradicional cuando no corresponda.

Calcular:

```text
Win Rate
Loss Rate
Average Payout
Break-even Win Rate
Expected Value
Total Stake
Net Profit
ROI
Longest Win Streak
Longest Loss Streak
Performance by Expiration
Performance by Asset
Performance by Setup
CALL vs PUT
Performance by Hour
Performance by Session
```

Break-even:

```text
BE = 1 / (1 + payout_decimal)
```

Ejemplo payout 80%:

```text
1 / 1.8 = 55.56%
```

Mostrar:

```text
Actual WR       63.2%
Break-even WR   55.6%

Edge            +7.6pp
```

---

# 28. MARKET DATA

Crear abstracción:

```text
IMarketDataProvider
```

Métodos aproximados:

```text
GetSymbols()
GetCandles()
GetLatestPrice()
SubscribeQuotes()
```

No acoplar el sistema a un proveedor concreto.

Modelo Candle:

```text
symbol
timeframe
timestamp
open
high
low
close
volume
```

Timeframes:

```text
M1
M5
M15
M30
H1
H4
D1
```

El Pattern Scanner debe consumir exclusivamente esta abstracción.

---

# 29. ECONOMIC CALENDAR

Crear módulo desacoplado:

```text
/calendar/economic
```

Entidades:

```text
country
currency
title
impact
event_time
forecast
previous
actual
```

Impact:

```text
LOW
MEDIUM
HIGH
```

Integrarlo posteriormente con:

- Journal;
- Planner;
- Risk;
- Alerts.

Ejemplo:

```text
HIGH IMPACT NEWS

USD CPI
14 minutos

Tu plan EUR/USD podría verse afectado.
```

---

# 30. AI COPILOT

No desarrollar IA primero.

Construirla DESPUÉS de tener datos suficientes.

Crear interfaces:

```text
IAIProvider
ITradingInsightService
```

El Copilot debe poder consultar datos autorizados del usuario:

```text
Accounts
Trades
Strategies
Journal
Risk
Analytics
Patterns
```

Preguntas futuras:

```text
¿Por qué estoy perdiendo este mes?

¿Cuál es mi mejor horario?

¿Cuál es mi peor setup?

¿Cómo rinde mi Gartley M5?

¿Qué pasa después de dos pérdidas consecutivas?

Compara junio contra julio.

¿Qué errores aparecen con mayor frecuencia?
```

Nunca permitir que el modelo invente métricas.

Las métricas deben venir del backend analítico.

La IA interpreta.

El motor analítico calcula.

---

# 31. IMPORTACIONES

V1 debe permitir importar operaciones desde CSV.

Crear:

```text
ImportJob
ImportMapping
ImportError
```

Flujo:

```text
Upload
    ↓
Preview
    ↓
Map Columns
    ↓
Validate
    ↓
Import
    ↓
Summary
```

Detectar duplicados.

No duplicar trades accidentalmente.

Crear mecanismo de:

```text
external_trade_id
import_hash
```

---

# 32. API

Versionar API:

```text
/api/v1/
```

Ejemplos:

```text
GET    /api/v1/accounts
POST   /api/v1/accounts
GET    /api/v1/accounts/{id}
PATCH  /api/v1/accounts/{id}

GET    /api/v1/trades
POST   /api/v1/trades
GET    /api/v1/trades/{id}
PATCH  /api/v1/trades/{id}

GET    /api/v1/analytics/overview
GET    /api/v1/analytics/performance
GET    /api/v1/analytics/time
GET    /api/v1/analytics/strategies
GET    /api/v1/analytics/patterns

GET    /api/v1/pnl-calendar

GET    /api/v1/risk/status
GET    /api/v1/risk/rules
PUT    /api/v1/risk/rules

GET    /api/v1/strategies
POST   /api/v1/strategies

GET    /api/v1/scanner/patterns
GET    /api/v1/scanner/patterns/{id}

GET    /api/v1/alerts
PATCH  /api/v1/alerts/{id}

GET    /api/v1/plans
POST   /api/v1/plans
```

Usar:

- pagination;
- filters;
- sorting;
- consistent error format;
- validation;
- correlation IDs.

---

# 33. SEGURIDAD

Implementar:

- password hashing seguro;
- JWT short-lived;
- refresh token rotation;
- revoke tokens;
- rate limiting;
- input validation;
- authorization policies;
- workspace isolation;
- audit logs;
- CORS restringido;
- secure headers;
- secrets mediante environment variables.

Nunca persistir:

- passwords en texto plano;
- API secrets sin cifrado;
- tokens sin protección.

Preparar 2FA.

---

# 34. TESTS

Cada módulo debe incluir:

```text
Unit Tests
Integration Tests
API Tests
```

Especial atención a:

```text
P&L calculations
R calculations
Binary payout
Drawdown
Profit Factor
Expectancy
Calendar aggregation
Workspace isolation
Risk Rules
Harmonic Ratios
Pattern state transitions
Duplicate imports
```

El scanner debe tener datasets de prueba determinísticos.

Ejemplo:

```text
Known Gartley
Expected:
pattern = GARTLEY
direction = BULLISH
status = VALID
```

---

# 35. SEED DATA

Crear datos demo suficientemente realistas.

Workspace:

```text
Demo Trader
```

Accounts:

```text
FTMO Challenge
IC Markets Real
Binary Real
Binance
```

Crear:

- 50+ trades;
- ganancias;
- pérdidas;
- Forex;
- binarias;
- crypto;
- varias estrategias;
- diferentes días;
- diferentes sesiones;
- journals;
- risk snapshots;
- patrones.

Así todas las pantallas serán visualmente comprobables.

---

# 36. RESPONSIVE

Prioridad:

```text
Desktop
1440px+
```

Pero arquitectura responsive para:

```text
Laptop
Tablet
Mobile
```

No intentar introducir toda la densidad del desktop en mobile.

En mobile priorizar:

```text
Dashboard
Alerts
Trades
Calendar
Journal
```

---

# 37. PERFORMANCE

Evitar:

```text
SELECT *
```

en consultas analíticas grandes.

Usar:

- indexes;
- projections;
- pagination;
- aggregation queries;
- caching solo cuando sea necesario.

Indexes importantes:

```text
workspace_id
account_id
instrument_id
opened_at
closed_at
strategy_id
setup_id
pattern_type
timeframe
detected_at
```

---

# 38. OBSERVABILIDAD

Implementar logging estructurado.

Registrar:

```text
request
error
import
pattern_detection
risk_violation
background_job
```

No registrar secretos.

Crear health checks:

```text
/health
/health/ready
```

---

# 39. DOCKER

Crear:

```text
docker-compose.yml
```

Servicios mínimos:

```text
frontend
backend
postgres
```

Opcional posteriormente:

```text
redis
worker
```

Crear:

```text
.env.example
```

Nunca versionar secretos reales.

README debe permitir levantar el sistema desde cero.

---

# 40. MIGRACIONES

Las migraciones deben estar versionadas.

Nunca modificar manualmente BD sin migración.

Agregar seed independiente del schema migration cuando sea posible.

---

# 41. FASES DE DESARROLLO

NO implementar todo simultáneamente.

Trabajar exactamente en este orden.

## FASE 0

Foundation:

```text
Repository
Docker
Frontend
Backend
PostgreSQL
Authentication
Workspace
Design System
Navigation
Audit
```

Acceptance:

```text
docker compose up
→ login
→ dashboard shell
→ authenticated navigation
```

---

## FASE 1

Accounts.

Debe quedar completamente funcional antes de avanzar.

---

## FASE 2

Operations.

Forex + Binary como prioridad.

---

## FASE 3

Dashboard.

Únicamente con datos reales.

---

## FASE 4

P&L Calendar.

Month + Year + Heatmap.

---

## FASE 5

Journal + Discipline.

---

## FASE 6

Risk Center.

---

## FASE 7

Analytics.

---

## FASE 8

Strategies + Playbook.

---

## FASE 9

Market Data abstraction.

---

## FASE 10

Harmonic Pattern Scanner.

---

## FASE 11

Alert Center.

---

## FASE 12

Trade Planner.

---

## FASE 13

Economic Calendar.

---

## FASE 14

CSV Imports + broker adapters.

---

## FASE 15

AI Copilot.

---

## FASE 16

Replay / Simulator.

No implementarlo antes.

---

# 42. REGLA DE IMPLEMENTACIÓN POR MÓDULO

Para CADA módulo:

### 1. Define domain

```text
entities
value objects
enums
rules
events
```

### 2. Define database

```text
tables
relationships
indexes
constraints
```

### 3. Implement backend

```text
commands
queries
services
API
validation
```

### 4. Implement frontend

```text
routes
components
forms
tables
charts
filters
empty states
loading states
error states
```

### 5. Tests

```text
unit
integration
API
```

### 6. Seed

Crear datos demo.

### 7. Documentation

Actualizar README y documentación del módulo.

### 8. Verification

Ejecutar:

```text
build
lint
tests
docker
```

No avanzar con errores.

---

# 43. UX STATES OBLIGATORIOS

Toda pantalla debe contemplar:

```text
Loading
Empty
Populated
Error
No Permission
Offline/Unavailable cuando corresponda
```

Nunca construir únicamente el happy path.

---

# 44. FORMATO DE RESPUESTA DEL AGENTE

Antes de desarrollar cada fase, responde:

```text
PHASE
MODULE
OBJECTIVE

DOMAIN CHANGES
DATABASE CHANGES
BACKEND CHANGES
FRONTEND CHANGES
TESTS

FILES TO CREATE
FILES TO MODIFY

RISKS
```

Después implementa.

Al terminar:

```text
IMPLEMENTED

FILES CREATED
FILES MODIFIED

MIGRATIONS

ENDPOINTS

UI ROUTES

TESTS

HOW TO TEST

KNOWN LIMITATIONS

NEXT MODULE
```

---

# 45. PROHIBICIONES

NO:

- crear microservicios prematuramente;
- construir ejecución de órdenes;
- añadir funcionalidades fuera del roadmap sin necesidad;
- hacer todo el producto en un solo commit;
- duplicar lógica P&L entre frontend/backend;
- calcular métricas críticas en frontend;
- mezclar lógica Forex y Binary;
- inventar información de mercado;
- presentar Pattern Score como probabilidad;
- acoplar scanner a proveedor específico;
- hardcodear workspace;
- hardcodear currency;
- hardcodear broker;
- guardar datos financieros solo en localStorage;
- utilizar mocks permanentes después de implementar el backend;
- avanzar dejando tests rotos;
- crear componentes gigantes;
- crear controllers gigantes;
- crear servicios God Object.

---

# 46. PRINCIPIOS DE CÓDIGO

Priorizar:

```text
SOLID
Clean Architecture pragmática
DDD donde aporte valor
DRY sin sobre-abstracción
Strong typing
Immutability donde corresponda
Explicit domain rules
Testability
Observability
```

Evitar overengineering.

---

# 47. OBJETIVO DE EXPERIENCIA

El usuario debería comenzar su día viendo:

```text
Dashboard
    ↓
Calendar / Market information
    ↓
Scanner
    ↓
Opportunity
    ↓
Trade Plan
    ↓
Risk Check
    ↓
External Broker
    ↓
Register Trade
    ↓
Journal
    ↓
Analytics
    ↓
Improve Strategy
```

Este es el loop central del producto.

Todo diseño y arquitectura debe ayudar a este flujo.

---

# 48. VISIÓN DEL PRODUCTO

Jade Trader OS no debe sentirse como:

> “una app para guardar trades”.

Debe sentirse como:

> “el sistema operativo personal del trader”.

Debe responder continuamente cuatro preguntas:

```text
1. ¿Cómo estoy rindiendo?

2. ¿Cuánto riesgo estoy tomando?

3. ¿Qué patrones existen en mi forma de operar?

4. ¿Qué debo mejorar?
```

Y eventualmente una quinta:

```text
5. ¿Qué oportunidades del mercado coinciden con mis estrategias?
```

---

# 49. PRIMERA TAREA

NO intentes implementar todos los módulos.

Empieza únicamente con:

```text
PHASE 0 — FOUNDATION
```

Construye:

1. estructura del repositorio;
2. backend;
3. frontend;
4. PostgreSQL;
5. Docker Compose;
6. autenticación;
7. workspace;
8. layout principal;
9. sidebar;
10. top navigation;
11. design system;
12. health checks;
13. logging;
14. migraciones;
15. seed inicial;
16. tests de infraestructura.

Cuando Phase 0 esté funcionando completamente, entrega el resultado y continúa con:

```text
PHASE 1 — ACCOUNTS
```

No saltes fases.

La aplicación debe permanecer ejecutable y testeable al finalizar **cada fase**.

El objetivo no es construir rápido una demo.

El objetivo es construir progresivamente una plataforma de trading profesional, mantenible, escalable y suficientemente robusta para convertirse en el único centro de análisis, riesgo, seguimiento, journal y mejora continua utilizado por el trader.