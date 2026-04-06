using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenClawManager.Common.Uninstall;

public enum UninstallResult
{
    Success,                          // 完全成功
    CliNotFoundButCleaned,            // CLI 不存在，但手动清理成功
    CliNotFoundAndCleanupFailed,      // CLI 不存在，手动清理失败
    OfficialUninstallFailed,          // 官方卸载失败，但手动清理部分成功或失败
    Partial,                          // 部分成功（官方成功但手动失败，或其他混合情况）
    UninstallFailed                   // 彻底失败
}

public enum OpenClawInstallSource
{
    Npm,
    Pnpm,
    Yarn,
    Binary,
    Unknown
}