// API 基础地址
const API_BASE = '/api';

// 初始化日志服务
const logService = new LogService('logContainer', {
    apiBase: API_BASE,
    maxLines: 500,
    maxReconnect: 5
});

// 状态管理
const state = {
    isInstalled: false,
    isRunning: false
};

// DOM 元素
const elements = {
    statusIcon: document.getElementById('statusIcon'),
    statusText: document.getElementById('statusText'),
    statusDetail: document.getElementById('statusDetail'),
    btnInstall: document.getElementById('btnInstall'),
    btnStart: document.getElementById('btnStart'),
    btnStop: document.getElementById('btnStop'),
    btnUninstall: document.getElementById('btnUninstall')
};

// 更新 UI
function updateUI() {
    const { isInstalled, isRunning } = state;

    // 状态显示
    elements.statusIcon.classList.toggle('running', isRunning);
    elements.statusText.classList.toggle('running', isRunning);

    if (isRunning) {
        elements.statusText.textContent = 'ONLINE';
        elements.statusDetail.textContent = 'Gateway running on port 18789';
    } else if (isInstalled) {
        elements.statusText.textContent = 'STANDBY';
        elements.statusDetail.textContent = 'Installed, ready to start';
    } else {
        elements.statusText.textContent = 'OFFLINE';
        elements.statusDetail.textContent = 'Not installed';
    }

    // 按钮状态
    elements.btnInstall.disabled = isInstalled || isRunning;
    elements.btnStart.disabled = !isInstalled || isRunning;
    elements.btnStop.disabled = !isRunning;
    elements.btnUninstall.disabled = isRunning || !isInstalled;
}

// 检查状态
async function checkStatus() {
    try {
        const res = await fetch(`${API_BASE}/status`);
        if (!res.ok) throw new Error(`HTTP ${res.status}`);

        const data = await res.json();
        state.isInstalled = data.isInstalled;
        state.isRunning = data.isRunning;

        updateUI();
    } catch (err) {
        logService.append('error', `Status check failed: ${err.message}`);
    }
}

// 安装
elements.btnInstall.addEventListener('click', async () => {
    elements.btnInstall.disabled = true;
    logService.append('system', '>>> 开始安装 OpenClaw...');

    try {
        const res = await fetch(`${API_BASE}/install`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' }
        });

        const data = await res.json();
        if (data.success) {
            logService.append('success', '>>> 安装完成');
            await checkStatus();
            // 自动启动
            //setTimeout(() => startService(), 500);
        } else {
            logService.append('error', `安装失败: ${data.error || 'Unknown'}`);
        }
    } catch (err) {
        logService.append('error', `安装错误: ${err.message}`);
    }

    updateUI();
});

// 启动
elements.btnStart.addEventListener('click', startService);

async function startService() {
    elements.btnStart.disabled = true;
    logService.append('system', '>>> 启动 OpenClaw...');

    try {
        const res = await fetch(`${API_BASE}/start`, { method: 'POST' });
        const data = await res.json();

        if (data.success) {
            logService.append('success', '>>> 启动成功');
        } else {
            logService.append('error', `启动失败: ${data.error || 'Unknown'}`);
        }
    } catch (err) {
        logService.append('error', `启动错误: ${err.message}`);
    }

    await checkStatus();
}

// 停止
elements.btnStop.addEventListener('click', async () => {
    elements.btnStop.disabled = true;
    logService.append('system', '>>> 停止 OpenClaw...');

    try {
        const res = await fetch(`${API_BASE}/stop`, { method: 'POST' });
        const data = await res.json();
        if (data.success) {
            logService.append('success', '>>> 已停止');
        } else {
            logService.append('error', `停止失败: ${data.error || 'Unknown'}`);
        }
    } catch (err) {
        logService.append('error', `停止错误: ${err.message}`);
    }

    await checkStatus();
});

// 卸载
elements.btnUninstall.addEventListener('click', async () => {
    if (!confirm('确认卸载 OpenClaw？所有配置将丢失。')) return;

    elements.btnUninstall.disabled = true;
    logService.append('system', '>>> 卸载 OpenClaw...');

    try {
        const res = await fetch(`${API_BASE}/uninstall`, { method: 'POST' });
        const data = await res.json();

        if (data.success) {
            logService.append('success', `>>> ${data.message}`);
        } else {
            logService.append('error', `卸载失败: ${data.message}`);
        }
    } catch (err) {
        logService.append('error', `卸载错误: ${err.message}`);
    } finally {
        elements.btnUninstall.disabled = false;
        await checkStatus();
    }
});

// 初始化
async function init() {
    updateUI();
    await checkStatus();

    // 启动日志流
    logService.start();

    // 定期刷新状态
    setInterval(checkStatus, 5000);
}

// 启动
init();