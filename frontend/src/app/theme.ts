import type { ThemeConfig } from 'antd';

const fontFamily = '-apple-system, BlinkMacSystemFont, "Segoe UI", "Microsoft YaHei", sans-serif';

export function createTheme(reducedMotion: boolean): ThemeConfig {
  return {
    token: {
      motion: !reducedMotion,
      colorPrimary: '#2563eb', colorInfo: '#2563eb', colorSuccess: '#15803d', colorWarning: '#a16207', colorError: '#dc2626',
      colorText: '#172033', colorTextSecondary: '#586579', colorTextPlaceholder: '#69778b', colorBgLayout: '#f5f7fb', colorBorder: '#d8e0eb', colorBorderSecondary: '#e4e9f1',
      borderRadius: 8, fontFamily, fontSize: 14, controlHeight: 38,
      motionDurationFast: '0.14s', motionDurationMid: '0.2s', motionDurationSlow: '0.24s', motionEaseInOut: 'cubic-bezier(.22, 1, .36, 1)',
      screenLG: 1024, screenLGMin: 1024, screenMDMax: 1023,
    },
    components: {
      Button: { primaryShadow: '0 2px 3px #2563eb18', fontWeight: 500 },
      Menu: { darkItemBg: '#151d2c', darkSubMenuItemBg: '#151d2c', darkItemSelectedBg: '#2563eb', darkItemColor: '#b4c0d3', darkItemHoverBg: '#243147', itemHeight: 46, itemBorderRadius: 8 },
      Card: { borderRadiusLG: 12, headerFontSize: 16 },
      Table: { headerBg: '#f8fafc', headerColor: '#586579', headerSplitColor: 'transparent', rowHoverBg: '#f6f9ff', cellPaddingBlock: 16, fontSize: 13 },
      Tag: { borderRadiusSM: 6 },
      Modal: { borderRadiusLG: 16, titleFontSize: 19 },
      Drawer: { footerPaddingBlock: 16 },
      Input: { activeShadow: '0 0 0 3px #2563eb14' },
      Select: { optionSelectedBg: '#eff6ff' },
    },
  };
}
