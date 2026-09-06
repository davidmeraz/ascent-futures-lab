# Reglas del proyecto

Aplicación local para investigar estrategias NQ/ES y exportar las mismas reglas a NinjaTrader. La comunicación y documentación para el usuario se escriben en español.

- `Core`: C# 13, mismas fuentes para net48 y net10.0, sin I/O ni dependencias de WPF, NinjaTrader o el analyzer. Indicadores y decisiones viven aquí.
- `Analyzer`: simulación y datos. `App`: un punto de entrada CLI/visual. `NinjaTrader`: adaptador y exportador de fuentes.
- Respetar las fases de `docs/PLAN.md`. Por petición explícita del usuario del 4 de septiembre de 2026, se adelanta una interfaz WPF sobre las funciones existentes. La importación, compilación y comparación reales en NinjaTrader siguen pendientes: esta excepción no aprueba la fase 1 ni autoriza presentar la v1 completa como terminada. El motor ampliado conserva su puerta de fase 1.
- Distinguir pruebas locales, compilación contra DLL de NinjaTrader e importación/ejecución reales dentro de NinjaTrader. Una no sustituye a otra.
- No ejecutar órdenes con un broker desde este proyecto. Las comprobaciones de integración usan históricos o simulación; no habilitar estrategias en cuentas reales.
- No alterar históricos originales ni modificar proyectos anteriores. Referenciar sus datos mediante rutas locales.
- Registrar configuraciones, versiones, hashes y convenciones de simulación. No presentar señales o ejecuciones simuladas como resultados garantizados.
- Comprobar casos de negocio y compatibilidad con ambos destinos. Usar versiones exactas de dependencias y conservar los lockfiles.
- Mantener cambios en NinjaTrader mínimos y verificables: solo artefactos propios necesarios para la integración, sin reemplazar scripts existentes.
