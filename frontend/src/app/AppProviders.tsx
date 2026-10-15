import { useSyncExternalStore } from 'react';
import type { ReactNode } from 'react';
import { BrowserRouter } from 'react-router-dom';
import { App as AntApp, ConfigProvider } from 'antd';
import zhCN from 'antd/locale/zh_CN';
import dayjs from 'dayjs';
import 'dayjs/locale/zh-cn';
import { createTheme } from './theme';

dayjs.locale('zh-cn');

const motionPreference = window.matchMedia('(prefers-reduced-motion: reduce)');
function subscribeMotionPreference(listener: () => void) {
  motionPreference.addEventListener('change', listener);
  return () => motionPreference.removeEventListener('change', listener);
}

export function AppProviders({ children }: { children: ReactNode }) {
  const reducedMotion = useSyncExternalStore(subscribeMotionPreference, () => motionPreference.matches);
  return <ConfigProvider button={{ autoInsertSpace: false }} locale={zhCN} theme={createTheme(reducedMotion)}>
    <AntApp><BrowserRouter>{children}</BrowserRouter></AntApp>
  </ConfigProvider>;
}
