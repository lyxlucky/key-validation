import { Alert, Button, Card, Typography, message } from 'antd';
import './integrations.css';

export function createVsCodeMcpConfig(origin: string) {
  return JSON.stringify({ servers: { keygate: { type: 'http', url: `${origin}/mcp` } } }, null, 2);
}
export function CopyText({ label, text }: { label: string; text: string }) {
  const [notice, holder] = message.useMessage();
  return <>{holder}<Button size="small" onClick={async () => {
    try { await navigator.clipboard.writeText(text); notice.success('已复制'); }
    catch { notice.error('复制失败，请手动选择并复制下方内容'); }
  }}>{label}</Button></>;
}
const prompts = [
  '请用 Python 编写在线授权接入：先搜索快速开始资料，再获取激活、心跳和退出接口及 online-license 示例，并在每次受保护操作前检查租约和 features。',
  '请查询 C# 与 JavaScript 在线授权示例，说明设备标识持久保存、会话令牌仅驻留内存以及明确拒绝或租约到期的处理。',
  '请查询 CLIENT_UPGRADE_REQUIRED 的排查指引，并核对版本检查接口和错误处理规则。',
];
export function McpIntegrationPanel() {
  const url = `${window.location.origin}/mcp`;
  const config = createVsCodeMcpConfig(window.location.origin);
  const command = `claude mcp add --transport http --scope project keygate "${url}"`;
  return <div className="integration-guide mcp-panel">
    <Card title="开发接入助手 MCP">
      <p>通过 Streamable HTTP 连接 AI 编程助手，读取与接入说明相同的公开开发资料。匿名、免凭证、只读。</p>
      <Typography.Text code copyable data-testid="mcp-url">{url}</Typography.Text>
      <Alert className="operations-block" type="info" showIcon title="仅提供公开开发资料" description="不查询真实应用、客户、卡密或订单，不执行激活或业务操作。无需创建 API 凭证，也无需把卡密或会话令牌提供给助手。" />
      <ul><li>搜索与读取资料：search_integration_docs、get_integration_doc</li><li>接口与代码示例：get_api_reference、get_code_example（C# / Python / Node.js，online-license）</li><li>错误排查：get_troubleshooting_guide；资源：keygate://docs/&#123;id&#125;</li></ul>
    </Card>
    <div className="integration-guide-grid">
      <Card title="VS Code 工作区配置">
        <p>写入当前工作区的 <code>.vscode/mcp.json</code>，在 VS Code 中启动服务器并确认工作区信任。</p>
        <CopyText label="复制 VS Code 配置" text={config} /><pre className="mcp-config">{config}</pre>
        <a href="https://code.visualstudio.com/docs/agents/reference/mcp-configuration" target="_blank" rel="noreferrer">VS Code 官方 MCP 配置参考</a>
      </Card>
      <Card title="Claude Code 项目配置">
        <p>在项目目录的终端执行以下命令。<code>--scope project</code> 将配置保存到项目根目录 <code>.mcp.json</code>；在 Claude Code 中确认项目服务器并用 <code>/mcp</code> 检查连接。</p>
        <CopyText label="复制 Claude Code 命令" text={command} /><pre className="mcp-config">{command}</pre>
        <a href="https://code.claude.com/docs/en/mcp" target="_blank" rel="noreferrer">Claude Code 官方 HTTP 接入与配置范围说明</a>
      </Card>
    </div>
    <Card title="示例提问"><p>连接后，把下列提问发送给编程助手。应用标识等运行参数由开发者在本机配置。</p>{prompts.map((prompt, index) => <div className="integration-example" key={prompt}><div className="integration-example-heading"><span>提问 {index + 1}</span><CopyText label={`复制提问 ${index + 1}`} text={prompt} /></div><p className="mcp-prompt">{prompt}</p></div>)}</Card>
  </div>;
}
