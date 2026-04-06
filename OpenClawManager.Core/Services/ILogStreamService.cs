using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenClawManager.Core.Services;

public interface ILogStreamService
{
    Task StreamLogsAsync(HttpResponse response, CancellationToken ct);
}
