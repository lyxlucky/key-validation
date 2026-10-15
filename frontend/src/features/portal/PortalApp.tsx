import { Activity, useEffect, useRef, useState } from 'react';
import { Alert, App as AntApp, Button, Card, Spin } from 'antd';
import { Navigate, useLocation } from 'react-router-dom';
import { PortalLayout } from './PortalLayout';
import { PortalLogin } from './PortalLogin';
import { PortalProvider, usePortal } from './PortalProvider';
import { PortalOrders } from './PortalOrders';
import { PortalLicenses } from './PortalLicenses';
import { PortalRequests } from './PortalRequests';
import './portal.css';
import '../commerce/commerce.css';

const pages = { orders: <PortalOrders />, licenses: <PortalLicenses />, requests: <PortalRequests /> };
type PortalPage = keyof typeof pages;
function PortalPages({ active }: { active: PortalPage }) {
  const [visited, setVisited] = useState<PortalPage[]>([active]);
  const previous = useRef(active);
  if (!visited.includes(active)) setVisited(current => [...current, active]);
  useEffect(() => {
    if (previous.current !== active && !document.activeElement?.closest('[role="tablist"]')) document.getElementById('portal-main')?.focus({ preventScroll: true });
    previous.current = active;
  }, [active]);
  return <div className="portal-panels">{visited.map(key => <Activity key={key} mode={key === active ? 'visible' : 'hidden'}><AntApp component={false}><div className={`portal-pane${key === active ? ' is-active' : ''}`}>{pages[key]}</div></AntApp></Activity>)}</div>;
}
function PortalScreen() {
  const auth = usePortal();
  const location = useLocation();
  if (auth.loading) return <div className="full-loading screen-enter"><Spin size="large" description="正在连接自助服务…" /></div>;
  if (auth.session && !auth.customer) return <main className="portal-login"><Card title="暂时无法连接自助服务">
    <Alert type="error" showIcon title={auth.error ?? '未能恢复会话，请重试连接。'} className="query-error" />
    <Button type="primary" onClick={auth.retryConnection}>重试连接</Button>
    <Button onClick={() => void auth.logout()} loading={auth.busy}>退出自助服务</Button>
  </Card></main>;
  if (!auth.session || !auth.customer) return <div className="screen-enter"><PortalLogin busy={auth.busy} error={auth.error} login={auth.login} /></div>;
  const key = location.pathname.split('/')[2];
  if (!Object.prototype.hasOwnProperty.call(pages, key)) return <Navigate to="/portal/orders" replace />;
  return <PortalLayout customerName={auth.customer.name || '我的授权'} expiresAt={auth.session.expiresAt} busy={auth.busy} logout={() => void auth.logout()}>
    {auth.error && <Alert className="query-error" type="error" showIcon title={auth.error} />}
    <PortalPages key={`${auth.customer.id}:${auth.session.expiresAt}`} active={key as PortalPage} />
  </PortalLayout>;
}
export function PortalApp() { return <PortalProvider><PortalScreen /></PortalProvider>; }
