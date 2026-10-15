import { Activity, useLayoutEffect, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { App as AntApp, Button, Dropdown } from 'antd';
import { CloseOutlined, DownOutlined, SafetyCertificateOutlined } from '@ant-design/icons';
import { useLocation, useNavigate } from 'react-router-dom';
import type { Location } from 'react-router-dom';

export interface WorkspacePage {
  key: string;
  label: string;
  icon: ReactNode;
  group?: 'licensing' | 'commerce' | 'operations' | 'system';
  permission?: string;
  anyPermissions?: string[];
  adminOnly?: boolean;
  globalScopeOnly?: boolean;
  anyPermission?: boolean;
  element: ReactNode;
}
interface WorkspaceTab { id: number; location: Location; label: string }
let nextTabId = 0;
const address = (location: Location) => location.pathname + location.search + location.hash;

export function useWorkspace(pages: WorkspacePage[], home: string) {
  const location = useLocation();
  const navigate = useNavigate();
  const requestedPath = useRef(location.pathname);
  // History advances before React commits a navigation. Consecutive clicks
  // compare against that intent; Back/Forward and external visits resync here.
  useLayoutEffect(() => { requestedPath.current = location.pathname; }, [location]);
  const createTab = (next: Location): WorkspaceTab => {
    const page = pages.find(item => item.key === next.pathname);
    return { id: ++nextTabId, location: next, label: page?.label ?? '访问受限' };
  };
  const [state, setState] = useState(() => ({ observed: location, tabs: [createTab(location)] }));
  // Synchronize before committing a new location. Each retained Routes tree receives
  // its own location snapshot, so another tab cannot reset its search-param state.
  if (state.observed !== location) {
    const found = state.tabs.some(tab => tab.location.pathname === location.pathname);
    setState({
      observed: location,
      tabs: found ? state.tabs.map(tab => tab.location.pathname === location.pathname ? { ...tab, location } : tab) : [...state.tabs, createTab(location)],
    });
  }
  const visit = (key: string) => {
    if (key === requestedPath.current) return;
    const tab = state.tabs.find(item => item.location.pathname === key);
    requestedPath.current = key;
    navigate(tab ? address(tab.location) : key);
  };
  const close = (kind: 'current' | 'others' | 'all', key = location.pathname) => {
    const index = state.tabs.findIndex(tab => tab.location.pathname === key);
    const remaining = state.tabs.filter(tab => kind === 'others' ? tab.location.pathname === key : kind === 'current' && tab.location.pathname !== key);
    setState(current => ({ ...current, tabs: remaining }));
    if (!remaining.some(tab => tab.location.pathname === location.pathname)) {
      const next = remaining[Math.max(0, index - 1)] ?? remaining[0];
      requestedPath.current = next?.location.pathname ?? home;
      navigate(next ? address(next.location) : home);
    }
  };
  return { tabs: state.tabs, active: location.pathname, visit, close };
}

type WorkspaceState = ReturnType<typeof useWorkspace>;

export function WorkspaceTabBar({ workspace }: { workspace: WorkspaceState }) {
  const strip = useRef<HTMLDivElement>(null);
  const { tabs, active, visit, close } = workspace;
  useLayoutEffect(() => {
    strip.current?.querySelector('[aria-selected="true"]')?.closest('.workspace-tab')?.scrollIntoView({ block: 'nearest', inline: 'nearest', behavior: 'instant' });
  }, [active, tabs.length]);
  function move(event: React.KeyboardEvent, index: number) {
    let next: number;
    if (event.key === 'ArrowLeft') next = (index + tabs.length - 1) % tabs.length;
    else if (event.key === 'ArrowRight') next = (index + 1) % tabs.length;
    else if (event.key === 'Home') next = 0;
    else if (event.key === 'End') next = tabs.length - 1;
    else if (event.key === 'Delete') { event.preventDefault(); close('current', tabs[index].location.pathname); return; }
    else return;
    event.preventDefault();
    visit(tabs[next].location.pathname);
    document.getElementById(`workspace-tab-${tabs[next].id}`)?.focus({ preventScroll: true });
  }
  return <div className="workspace-tabs-bar">
    <div ref={strip} className="workspace-tabs" role="tablist" aria-label="工作区标签">
      {tabs.map((tab, index) => <div key={tab.id} className={`workspace-tab${tab.location.pathname === active ? ' is-active' : ''}`}>
        <button type="button" id={`workspace-tab-${tab.id}`} role="tab" title={tab.label} aria-selected={tab.location.pathname === active} aria-controls={`workspace-panel-${tab.id}`} tabIndex={tab.location.pathname === active ? 0 : -1} onClick={() => visit(tab.location.pathname)} onKeyDown={event => move(event, index)}>
          <span className="workspace-tab-indicator" aria-hidden="true" /><span className="workspace-tab-label">{tab.label}</span>
        </button>
        <button type="button" className="workspace-tab-close" aria-label={`关闭${tab.label}标签`} onClick={() => close('current', tab.location.pathname)}><CloseOutlined aria-hidden /></button>
      </div>)}
    </div>
    <Dropdown trigger={['click']} menu={{ items: [
      { key: 'current', label: '关闭当前标签' },
      { key: 'others', label: '关闭其他标签', disabled: tabs.length < 2 },
      { key: 'all', label: '关闭全部标签' },
    ], onClick: ({ key }) => close(key as 'current' | 'others' | 'all') }}>
      <Button type="text" className="workspace-tabs-menu" icon={<DownOutlined />} aria-label="标签管理" />
    </Dropdown>
  </div>;
}

function RetainedPanel({ tab, active, children }: { tab: WorkspaceTab; active: boolean; children: ReactNode }) {
  const pane = useRef<HTMLElement>(null);
  const scroll = useRef(0);
  useLayoutEffect(() => { if (active && pane.current) pane.current.scrollTop = scroll.current; }, [active]);
  return <section ref={pane} id={`workspace-panel-${tab.id}`} role="tabpanel" aria-labelledby={`workspace-tab-${tab.id}`} aria-hidden={!active} inert={!active} hidden={!active}
    className={`workspace-pane${active ? ' is-active' : ''}`}
    onScroll={event => { if (active) scroll.current = event.currentTarget.scrollTop; }}>
    <Activity mode={active ? 'visible' : 'hidden'}>
      <AntApp component={false}><div className="workspace-page-content">{children}<footer className="page-footer"><span><SafetyCertificateOutlined /> KeyGate <span className="footer-dot">·</span> 软件授权管理平台</span><span>未标注时区的时间按当前设备时区显示</span></footer></div></AntApp>
    </Activity>
  </section>;
}

export function WorkspacePanels({ workspace, renderPage }: { workspace: WorkspaceState; renderPage: (location: Location, tabId: number) => ReactNode }) {
  return <div className="workspace-panels">{workspace.tabs.map(tab => <RetainedPanel key={tab.id} tab={tab} active={tab.location.pathname === workspace.active}>{renderPage(tab.location, tab.id)}</RetainedPanel>)}</div>;
}
