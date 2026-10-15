import type { ReactNode } from 'react';
import { Button, Tabs } from 'antd';
import { LogoutOutlined, SafetyCertificateOutlined } from '@ant-design/icons';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { dateText } from '../../components/common';

export function PortalLayout({ children, customerName, expiresAt, busy, logout }: {
  children: ReactNode; customerName: string; expiresAt: string; busy: boolean; logout: () => void;
}) {
  const location = useLocation();
  const navigate = useNavigate();
  const active = location.pathname.split('/')[2] ?? 'orders';
  return <div className="portal-shell screen-enter">
    <a href="#portal-main" className="skip-link">跳到主内容</a>
    <header className="portal-topbar"><div className="portal-topbar-inner">
      <Link className="portal-brand" to="/portal"><SafetyCertificateOutlined aria-hidden /><span>KeyGate</span><span className="portal-product-label">客户自助</span></Link>
      <Button icon={<LogoutOutlined aria-hidden />} loading={busy} onClick={logout}>退出自助服务</Button>
    </div></header>
    <div className="portal-content">
      <div className="portal-welcome"><h1>{customerName}</h1><p>查看交付记录、授权状态和申请进度。</p></div>
      <nav aria-label="客户自助导航"><Tabs activeKey={active} onChange={key => navigate(`/portal/${key}`)} items={[
        { key: 'orders', label: '我的订单' }, { key: 'licenses', label: '我的授权' }, { key: 'requests', label: '我的申请' },
      ]} /></nav>
      <main id="portal-main" tabIndex={-1}>{children}</main>
      <footer className="portal-footer"><span>本次登录有效至 {dateText(expiresAt)}</span><span>时间按当前设备时区显示</span></footer>
    </div>
  </div>;
}
