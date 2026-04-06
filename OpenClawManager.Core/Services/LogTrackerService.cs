using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenClawManager.Core.Services;

public class LogTrackerService
{
    private readonly ConcurrentDictionary<string, long> _positions = new();

    public long GetPosition(string file) =>
        _positions.TryGetValue(file, out var pos) ? pos : 0;

    public void Update(string file, long position) =>
        _positions[file] = position;

    public void Reset(string file) =>
        _positions[file] = 0;
}