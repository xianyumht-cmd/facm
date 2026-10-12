using System.Collections.Generic;
using FACM.AppHost;
using FACM.AppHost.Modules;

namespace FACM.Performance
{
    internal sealed class PerformanceModule : IFacmModule
    {
        private static readonly IReadOnlyList<string> NoDependencies = new string[0];
        private readonly PerformanceBudgetProvider _budgets = new PerformanceBudgetProvider();
        public const string ModuleId = "performance";
        public string Id { get { return ModuleId; } }
        public IReadOnlyList<string> Dependencies { get { return NoDependencies; } }
        public PerformanceBudgetProvider Budgets { get { return _budgets; } }
        public void Initialize() { }
        public void Dispose() { }
    }
}
