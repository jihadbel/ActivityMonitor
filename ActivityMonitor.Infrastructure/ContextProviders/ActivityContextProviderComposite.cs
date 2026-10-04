using System;
using System.Collections.Generic;
using System.Linq;
using ActivityMonitor.Core.Interfaces;

namespace ActivityMonitor.Infrastructure.ContextProviders;

public class ActivityContextProviderComposite
{
    private readonly List<IActivityContextProvider> _providers;

    public ActivityContextProviderComposite(IEnumerable<IActivityContextProvider> providers)
    {
        _providers = providers.OrderBy(p => p.Order).ToList();
    }

    public string? ResolveContext(string processName, IntPtr windowHandle, string windowTitle)
    {
        foreach (var provider in _providers)
        {
            try
            {
                if (provider.CanHandle(processName, windowHandle))
                {
                    var ctx = provider.GetContext(processName, windowHandle, windowTitle);
                    if (!string.IsNullOrWhiteSpace(ctx))
                    {
                        return ctx;
                    }
                }
            }
            catch
            {
                // Silently skip failing provider
            }
        }

        return null;
    }
}
