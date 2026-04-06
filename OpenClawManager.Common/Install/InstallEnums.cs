using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenClawManager.Common.Install;

// 安装结果枚举
public enum InstallResult
{
    Success,                // 安装成功
    AlreadyInstalled,       // OpenClaw 已安装
    NodeJsNotInstalled,     // Node.js 未安装或版本不符
    NodeJsInstallFailed,    // Node.js 自动安装失败
    PackageManagerNotFound, // 未找到 npm 或 pnpm
    InstallFailed,          // OpenClaw 安装失败
    UnknownError            // 未知错误
}