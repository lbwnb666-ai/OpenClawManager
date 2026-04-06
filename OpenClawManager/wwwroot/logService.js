// 日志服务封装
class LogService {
    constructor(containerId, options = {}) {
        this.container = document.getElementById(containerId);
        this.apiBase = options.apiBase || '/api/demo';
        this.maxLines = options.maxLines || 500;
        this.eventSource = null;
        this.reconnectAttempts = 0;
        this.maxReconnect = options.maxReconnect || 5;
        this.reconnectDelay = options.reconnectDelay || 2000;
    }

    // 启动日志流
    start() {
        this.connect();
    }

    // 停止日志流
    stop() {
        if (this.eventSource) {
            this.eventSource.close();
            this.eventSource = null;
            this.append('system', '>>> 日志流已断开');
        }
    }

    // 建立 SSE 连接
    connect() {
        const url = `${this.apiBase}/logs/stream`;

        this.eventSource = new EventSource(url);

        // 连接成功
        this.eventSource.onopen = () => {
            this.reconnectAttempts = 0;
            this.append('system', '>>> 日志流已连接');
        };

        // 标准消息（默认 event: message）
        this.eventSource.onmessage = (e) => {
            this.append('log', this.unescape(e.data));
        };

        // 历史日志
        this.eventSource.addEventListener('history', (e) => {
            this.append('history', this.unescape(e.data));
        });

        // 实时日志
        this.eventSource.addEventListener('log', (e) => {
            this.append('log', this.unescape(e.data));
        });

        // 日志轮转（新文件）
        this.eventSource.addEventListener('rotate', (e) => {
            this.clear();
            this.append('system', this.unescape(e.data));
        });

        // 系统消息
        this.eventSource.addEventListener('system', (e) => {
            this.append('system', this.unescape(e.data));
        });

        // 错误处理
        this.eventSource.onerror = (err) => {
            console.error('SSE error:', err);
            this.eventSource.close();
            this.handleReconnect();
        };
    }

    // 重连逻辑
    handleReconnect() {
        if (this.reconnectAttempts >= this.maxReconnect) {
            this.append('error', '>>> 日志流连接失败，请刷新页面重试');
            return;
        }

        this.reconnectAttempts++;
        const delay = Math.min(
            this.reconnectDelay * Math.pow(2, this.reconnectAttempts),
            30000
        );

        this.append('system', `>>> ${delay / 1000}秒后重连...`);

        setTimeout(() => this.connect(), delay);
    }

    // 添加日志行
    append(type, message) {
        const line = document.createElement('div');
        line.className = `log-line log-${type}`;

        // 解析日志内容
        const parsed = this.parseLog(message);
        line.innerHTML = parsed.html;

        this.container.appendChild(line);
        this.scrollToBottom();

        // 限制行数
        this.trimLines();
    }

    // 解析日志格式
    parseLog(message) {
        // 匹配: [2026-03-31 18:31:06.344 +08:00] [INF] message
        const match = message.match(/^\[([\d\-:\.\s\+]+)\]\s*\[(\w+)\]\s*(.+)$/);

        if (match) {
            const [, time, level, content] = match;
            const levelClass = this.getLevelClass(level);

            return {
                html: `
                    <span class="log-time">${this.escapeHtml(time)}</span>
                    <span class="log-level ${levelClass}">${this.escapeHtml(level)}</span>
                    <span class="log-content">${this.escapeHtml(content)}</span>
                `
            };
        }

        // 无法解析，原样显示
        return { html: `<span class="log-content">${this.escapeHtml(message)}</span>` };
    }

    // 日志级别映射
    getLevelClass(level) {
        const map = {
            'INF': 'info',
            'WRN': 'warning',
            'ERR': 'error',
            'DBG': 'debug',
            'FTL': 'fatal'
        };
        return map[level] || 'info';
    }

    // HTML 转义
    escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    // 转义还原
    unescape(data) {
        return data.replace(/\\n/g, '\n');
    }

    // 滚动到底部
    scrollToBottom() {
        this.container.scrollTop = this.container.scrollHeight;
    }

    // 限制行数
    trimLines() {
        while (this.container.children.length > this.maxLines) {
            this.container.removeChild(this.container.firstChild);
        }
    }

    // 清空日志
    clear() {
        this.container.innerHTML = '';
    }
}

// 导出
window.LogService = LogService;