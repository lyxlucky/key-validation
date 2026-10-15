import { Alert, Card, Collapse, Spin, Table, Typography } from 'antd';
import './integrations.css';
import { QueryError } from '../../components/common';
import { useDeveloperDocument, useDeveloperDocuments } from './developer-docs';
import { CopyText } from './McpIntegrationPanel';

function Example({ label, children }: { label: string; children: string }) {
  return <div className="integration-example"><div className="integration-example-heading"><span>{label}</span><Typography.Text copyable={{ text: children, tooltips: ['复制示例', '已复制'] }} /></div><pre>{children}</pre></div>;
}

// Render a deliberately small Markdown subset as React text nodes. Unknown
// markup stays visible text; links and raw HTML never become active content.
function inlineText(text: string) {
  return text.split(/(`[^`]+`)/g).map((part, index) => part.startsWith('`') && part.endsWith('`') ? <code key={index}>{part.slice(1, -1)}</code> : part);
}
function DeveloperMarkdown({ markdown }: { markdown: string }) {
  let codeIndex = 0;
  return <div className="developer-markdown">{markdown.split(/(```[^\n]*\n[\s\S]*?```)/g).map((block, index) => {
    if (block.startsWith('```')) {
      const newline = block.indexOf('\n');
      const text = block.slice(newline + 1, -3).replace(/\r?\n$/, '');
      const label = `复制代码块 ${++codeIndex}`;
      return <div className="integration-example" key={index}><div className="integration-example-heading"><span>{block.slice(3, newline).trim() || '代码'}</span><CopyText label={label} text={text} /></div><pre>{text}</pre></div>;
    }
    return block.split(/\r?\n/).filter(line => line.trim()).map((line, lineIndex) => {
      const key = `${index}-${lineIndex}`;
      const heading = /^(#{1,6})\s+(.+)$/.exec(line);
      if (heading) return <Typography.Title key={key} level={Math.min(heading[1].length + 1, 5) as 2 | 3 | 4 | 5}>{inlineText(heading[2])}</Typography.Title>;
      if (/^[-*]\s/.test(line)) return <ul key={key}><li>{inlineText(line.slice(2))}</li></ul>;
      return <p key={key}>{inlineText(line)}</p>;
    });
  })}</div>;
}
function SharedDeveloperDocument({ id, version }: { id: string; version: string }) {
  const query = useDeveloperDocument(id, version);
  return <><QueryError error={query.error} retry={query.refetch} />{query.isPending && <Spin aria-label="正在加载资料" />}{query.data && <><p className="integration-note">资料版本：{query.data.version}</p><DeveloperMarkdown markdown={query.data.markdown} /></>}</>;
}
function SharedDeveloperDocs() {
  const query = useDeveloperDocuments();
  return <Card title="共享开发资料" className="operations-block">
    <p>以下客户端接入资料与 MCP 读取同一版本目录，涵盖接口、在线示例、离线验证及错误排查。</p>
    <QueryError error={query.error} retry={query.refetch} />
    {query.isPending && <Spin aria-label="正在加载资料目录" />}
    {query.data && <Collapse defaultActiveKey={query.data.length ? [query.data[0].id] : []} items={query.data.map(doc => ({ key: doc.id, label: doc.title, children: <SharedDeveloperDocument id={doc.id} version={doc.version} /> }))} />}
  </Card>;
}

const signature = `secretBytes = Base64Decode(订阅创建或轮换时显示的签名密钥)
signingInput = UTF8(timestamp + "\\n" + rawBody)
expected = HexLower(HMAC_SHA256(secretBytes, signingInput))

使用固定时间比较 expected 与 X-KeyGate-Signature`;

const businessOrder = `POST /api/v1/orders
Authorization: Bearer <商家服务端 API 凭证>
Content-Type: application/json

{
  "requestId": "每笔业务生成并持久保存的 UUID",
  "applicationId": "凭证所属应用的 UUID",
  "customerId": "该应用中客户的 UUID",
  "planId": "卡种 UUID",
  "type": "issue",
  "quantity": 1,
  "unitPriceCents": 1200
}`;

const fulfillOrder = `POST /api/v1/orders/<orderId>/fulfill
Authorization: Bearer <商家服务端 API 凭证>
Content-Type: application/json

{ "requestId": "本次履约业务固定的 UUID" }

GET /api/v1/orders/<orderId>/delivery
Authorization: Bearer <商家服务端 API 凭证>`;

export function IntegrationGuide() {
  return <div className="integration-guide">
    <Card className="integration-intro">
      <div><span className="eyebrow">QUICK START</span><h2>把授权验证接入你的软件</h2><p>先在管理台创建应用和卡种，再由软件调用客户端接口验证卡密。激活成功后持续发送心跳，并在每次受保护操作前检查当前授权。</p></div>
      <div className="integration-base"><span>当前服务地址</span><Typography.Text code copyable>{window.location.origin}</Typography.Text><small>部署后使用对外可访问的 HTTPS 服务地址。</small></div>
    </Card>
    <SharedDeveloperDocs />
    <Collapse className="operations-block" items={[{ key: 'offline-key-setup', label: '服务端离线签名配置与密钥轮换', children: <div className="integration-webhook-guide">
      <p>使用 ECDSA P-256 密钥。配置 <code>Offline:CurrentKeyId</code> 为当前密钥 ID，在 <code>Offline:Keys:&#123;kid&#125;</code> 放置该 ID 对应的 PKCS#8 私钥 PEM；缺少配置时系统明确显示不可签发。私钥只部署在服务端，并妥善保护备份。</p>
      <p>轮换时先将新公钥可信分发给客户端，再切换服务端当前密钥 ID 与私钥；保留旧 ID 的 SPKI 公钥至旧许可证全部到期。<code>Offline:Enabled=false</code> 可停止服务端离线签发与核验，但不会即时改变已离线设备的权限。</p>
    </div> }]} />
    <Card title="商家服务端接入业务 API" className="operations-block">
      <p>在“API 凭证”标签页创建凭证，选择应用、可选渠道和业务权限。把一次展示的凭证保存在你的服务端配置中，通过 <code>Authorization: Bearer</code> 发送。软件客户端继续使用卡密激活接口。</p>
      <Table rowKey="permission" pagination={false} size="small" scroll={{ x: 640 }} dataSource={[
        { permission: 'licenses.read', capability: '读取范围内的授权与设备', endpoints: 'GET /api/v1/licenses 和 /licenses/{id}' },
        { permission: 'customers.manage', capability: '创建、更新和查询客户', endpoints: 'GET / POST /api/v1/customers；PATCH /customers/{id}' },
        { permission: 'orders.manage', capability: '创建订单、人工履约与读取交付', endpoints: 'GET / POST /api/v1/orders；POST /orders/{id}/fulfill' },
      ]} columns={[{ title: '凭证权限', dataIndex: 'permission', width: 185, render: value => <code>{value}</code> }, { title: '用途', dataIndex: 'capability', width: 230 }, { title: '接口', dataIndex: 'endpoints' }]} />
      <p>使用 <code>GET /api/v1/catalog</code> 读取允许的应用与卡种；订单专员可用 <code>GET /api/v1/orders/catalog</code> 读取客户名称和续期授权选项。分页接口接受 <code>page</code> 与 <code>pageSize</code>，返回 <code>items</code> 和 <code>total</code>。</p>
      <div className="integration-guide-grid"><Example label="创建发卡订单 · 金额以分为单位">{businessOrder}</Example><Example label="确认人工履约，再读取交付">{fulfillOrder}</Example></div>
      <Alert type="info" showIcon title="保存请求标识，才能安全重试" description="创建或更新客户、创建订单和履约时均提交 requestId。重试必须保持原标识和原内容；内容改变会返回 409。创建订单响应丢失时，可先用 GET /api/v1/orders?requestId=原标识 查询原订单，再决定是否重试。" />
      <p>续期订单使用 <code>type: renewal</code>、数量 1，并指定该客户的 <code>licenseId</code> 和整数小时对应的 <code>additionalSeconds</code>。履约后价格及授权条件保留创建时的快照；渠道订单采用渠道售价并扣减额度。</p>
      <p>卡密明文只在专用交付接口返回，默认保留 30 天。列表和事件不包含明文。每个凭证每分钟最多 60 次请求；遇到 429 按 <code>Retry-After</code> 延后重试。轮换或停用凭证后旧值立即失效，越界资源返回 404。</p>
    </Card>
    <Card title="让客户使用自助服务" className="operations-block">
      <p>在“客户管理”生成一次性展示的访问码，通过你核实过的交付渠道提供给客户。客户打开 <Typography.Text code copyable>{`${window.location.origin}/portal`}</Typography.Text>，即可查看自己的订单、交付和授权，并提交续期或换绑申请。</p>
      <p>门户使用独立的 8 小时会话。客户退出、访问码轮换或客户停用后，会话失效；申请由商家在“客户申请”页审核，客户不能自行修改价格或授权条件。</p>
    </Card>
    <Collapse className="operations-block" items={[{ key: 'webhook-guide', label: '接收 Webhook 业务事件', children: <div className="integration-webhook-guide">
      <p>在 Webhook 标签页选择应用、接收地址和事件，保存一次性签名密钥。接收地址使用已由管理员允许的 HTTPS 域名。</p>
      <p>读取原始请求体和 <code>X-KeyGate-Timestamp</code>、<code>X-KeyGate-Signature</code>、<code>X-KeyGate-Event-Id</code> 请求头，先校验签名，再处理事件。时间戳为 Unix 秒，接收方应限制允许的时间偏差，例如 5 分钟。</p>
      <Example label="签名校验算法">{signature}</Example>
      <p>签名包含换行符，<code>rawBody</code> 必须是收到的原始 JSON 字节内容；不要重新序列化后计算。密钥需要先 Base64 解码。</p>
      <Alert type="info" showIcon title="按事件 ID 幂等处理" description="同一事件可能重复或乱序到达。先持久记录事件 ID，避免重复执行业务；处理或可靠入队后返回 2xx。可在投递记录中查看失败并重试。" />
    </div> }]} />
  </div>;
}
