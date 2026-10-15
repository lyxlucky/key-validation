import { Tabs } from 'antd';
import { PageTitle } from '../../components/common';
import { WebhooksPanel } from './WebhooksPanel';
import { IntegrationGuide } from './IntegrationGuide';
import { ApiCredentialsPanel } from './ApiCredentialsPanel';
import { useAuth } from '../auth/AuthProvider';
import { McpIntegrationPanel } from './McpIntegrationPanel';

export function IntegrationsPage() {
  const auth = useAuth();
  return <><PageTitle title="开放集成" subtitle="查看接入说明，管理服务端 API 凭证和业务事件订阅。" />
    <Tabs animated={{ inkBar: true, tabPane: false }} items={[
      { key: 'guide', label: '接入说明', children: <IntegrationGuide /> },
      { key: 'mcp', label: 'MCP 接入', children: <McpIntegrationPanel /> },
      ...(auth.isAdmin ? [{ key: 'credentials', label: 'API 凭证', children: <ApiCredentialsPanel /> }] : []),
      ...(auth.hasPermission('integrations.manage') ? [{ key: 'webhooks', label: 'Webhook', children: <WebhooksPanel /> }] : []),
    ]} />
  </>;
}
