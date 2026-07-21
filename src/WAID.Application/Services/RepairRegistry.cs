using WAID.Application.Abstractions;

namespace WAID.Application.Services;

public sealed class RepairRegistry
{
    private readonly IReadOnlyDictionary<string, IRepairModule> _modules;

    public RepairRegistry(IEnumerable<IRepairModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        var registered = new Dictionary<string, IRepairModule>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in modules)
        {
            if (string.IsNullOrWhiteSpace(module.Id))
                throw new InvalidOperationException("Every repair module must have an id.");
            if (!registered.TryAdd(module.Id, module))
                throw new InvalidOperationException($"Duplicate repair module id: {module.Id}");
            module.Policy.Validate();
        }
        _modules = registered;
    }

    public IReadOnlyCollection<IRepairModule> All => _modules.Values.ToArray();

    public bool TryGet(string id, out IRepairModule? module) => _modules.TryGetValue(id, out module);
}
