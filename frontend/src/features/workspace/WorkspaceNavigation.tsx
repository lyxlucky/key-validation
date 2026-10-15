import { useEffect, useState } from 'react';
import { Menu } from 'antd';
import { ApiOutlined, KeyOutlined, SettingOutlined, TeamOutlined } from '@ant-design/icons';
import type { WorkspacePage } from './Workspace';

const groups = [
  { key: 'licensing', label: '授权管理', icon: <KeyOutlined /> },
  { key: 'commerce', label: '客户与渠道', icon: <TeamOutlined /> },
  { key: 'operations', label: '运营与集成', icon: <ApiOutlined /> },
  { key: 'system', label: '系统管理', icon: <SettingOutlined /> },
];

export function WorkspaceNavigation({ pages, active, collapsed = false, visit }: { pages: WorkspacePage[]; active: string; collapsed?: boolean; visit: (key: string) => void }) {
  const currentGroup = pages.find(page => page.key === active)?.group;
  const [openGroups, setOpenGroups] = useState<string[]>(currentGroup ? [currentGroup] : []);
  const [popupGroups, setPopupGroups] = useState<string[]>([]);
  useEffect(() => { if (currentGroup) setOpenGroups([currentGroup]); }, [active, currentGroup]);
  useEffect(() => { setPopupGroups([]); }, [collapsed, active]);
  const changeOpenGroups = (keys: string[]) => {
    const setGroups = collapsed ? setPopupGroups : setOpenGroups;
    setGroups(current => {
      const opened = keys.find(key => !current.includes(key));
      return opened ? [opened] : [];
    });
  };
  const item = ({ key, label, icon }: WorkspacePage) => ({ key, label, icon: <span aria-hidden="true" className="navigation-icon">{icon}</span> });
  const items = [
    ...pages.filter(page => !page.group).map(item),
    ...groups.filter(group => pages.some(page => page.group === group.key)).map(group => ({ ...group, icon: <span aria-hidden="true" className="navigation-icon">{group.icon}</span>, children: pages.filter(page => page.group === group.key).map(item) })),
  ];
  return <nav aria-label="主导航"><Menu theme="dark" mode="inline" inlineCollapsed={collapsed} inlineIndent={16} triggerSubMenuAction="click" selectedKeys={[active]}
    openKeys={collapsed ? popupGroups : openGroups} onOpenChange={changeOpenGroups} items={items}
    onClick={({ key }) => { setPopupGroups([]); visit(key); }} /></nav>;
}
