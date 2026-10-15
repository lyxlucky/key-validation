import { useState } from 'react';
import { App, Button, Tooltip } from 'antd';
import { CheckOutlined, CopyOutlined } from '@ant-design/icons';

export async function copyText(value: string) {
  if (navigator.clipboard?.writeText) {
    await navigator.clipboard.writeText(value);
    return;
  }
  const field = document.createElement('textarea');
  field.value = value;
  field.style.position = 'fixed';
  field.style.opacity = '0';
  document.body.appendChild(field);
  const focused = document.activeElement;
  try {
    field.select();
    if (!document.execCommand('copy')) throw new Error('Clipboard unavailable');
  } finally {
    field.remove();
    if (focused instanceof HTMLElement) focused.focus({ preventScroll: true });
  }
}

export function CopyValue({ value, label, compact = false }: {
  value: string | null | undefined; label: string; compact?: boolean;
}) {
  const { message } = App.useApp();
  const [copiedValue, setCopiedValue] = useState<string>();
  if (!value) return <>—</>;
  return <span className={`copy-value${compact ? ' is-compact' : ''}`}>
    <code title={value}>{value}</code>
    <Tooltip title={`复制${label}`}><Button type="text" size="small"
      aria-label={`复制${label}`} icon={copiedValue === value ? <CheckOutlined /> : <CopyOutlined />}
      onClick={async () => {
        try { await copyText(value); setCopiedValue(value); message.success(`${label}已复制`); }
        catch { message.error('复制失败，请手动选择文本或下载保存'); }
      }} /></Tooltip>
  </span>;
}
