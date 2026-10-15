import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert, App, Button, Card, Descriptions, Drawer, Form, Input, Modal, Space, Table, Tag } from 'antd';
import type { License, LicenseDetail, Page } from '../../lib/types';
import type { PortalRequest } from '../../lib/commerce-types';
import { dateText, emptyTable, LicenseStatus, QueryError } from '../../components/common';
import { useFormSnapshot } from '../../components/useFormSnapshot';
import { usePortal } from './PortalProvider';

interface RequestDraft { requestId: string; type: 'renewal' | 'rebinding'; licenseId: string; bindingId?: string }

export function PortalLicenses() {
  const { request } = usePortal();
  const cache = useQueryClient();
  const { message } = App.useApp();
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<string | null>(null);
  const [draft, setDraft] = useState<RequestDraft | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [form] = Form.useForm<{ note: string }>();
  useFormSnapshot(form, draft, !!draft, () => ({ note: '' }));
  const licenses = useQuery({ queryKey: ['portal-licenses', page], queryFn: () => request<Page<License>>(`/licenses?page=${page}&pageSize=20`) });
  const detail = useQuery({ queryKey: ['portal-license', selected], queryFn: () => request<LicenseDetail>(`/licenses/${selected}`), enabled: !!selected });
  const value = detail.data;
  async function submit(values: { note: string }) {
    if (!draft || busy) return;
    setBusy(true); setError(null);
    try { await request<PortalRequest>('/requests', { method: 'POST', body: { ...draft, note: values.note.trim() } }); setDraft(null); await cache.invalidateQueries({ queryKey: ['portal-requests'] }); message.success('申请已提交，等待商家处理'); }
    catch (caught) { setError((caught as Error).message); }
    finally { setBusy(false); }
  }
  function open(type: RequestDraft['type'], bindingId?: string) { if (!value) return; setError(null); setDraft({ requestId: crypto.randomUUID(), type, licenseId: value.license.id, bindingId }); }
  return <><QueryError error={licenses.error} retry={licenses.refetch} /><Card className="data-panel"><div className="panel-heading"><div><h2 className="panel-title">我的授权</h2><p className="panel-description">查看授权状态和设备，向商家申请续期或换绑。</p></div><Button onClick={() => licenses.refetch()}>刷新授权</Button></div>
    <Table<License> rowKey="id" dataSource={licenses.data?.items} loading={licenses.isLoading} locale={{ emptyText: emptyTable }} scroll={{ x: 720 }} pagination={{ current: page, pageSize: 20, total: licenses.data?.total, onChange: setPage, showSizeChanger: false }} columns={[
      { title: '授权标识', dataIndex: 'keyHint', width: 190 }, { title: '状态', dataIndex: 'status', width: 100, render: status => <LicenseStatus status={status} /> },
      { title: '到期时间', dataIndex: 'expiresAt', width: 180, render: dateText }, { title: '功能', dataIndex: 'features', render: (items: string[]) => items.length ? items.map(item => <Tag key={item}>{item}</Tag>) : '无附加功能' },
      { title: '操作', fixed: 'right', width: 90, render: (_, license) => <Button type="link" onClick={() => setSelected(license.id)}>详情</Button> },
    ]} />
  </Card><Drawer title="我的授权详情" open={!!selected} loading={detail.isLoading} size={740} onClose={() => { if (!busy) setSelected(null); }} closable={{ 'aria-label': '关闭', disabled: busy }} destroyOnHidden>
    <QueryError error={detail.error} retry={detail.refetch} />
    {value && <><Space size={12} wrap><strong>{value.license.keyHint}</strong><LicenseStatus status={value.license.status} /></Space>
      <Descriptions style={{ marginTop: 24 }} column={{ xs: 1, sm: 2 }} items={[
        { key: 'activated', label: '激活时间', children: dateText(value.license.activatedAt) }, { key: 'expires', label: '到期时间', children: dateText(value.license.expiresAt) },
        { key: 'devices', label: '绑定上限', children: `${value.license.maxDevices} 台设备` }, { key: 'online', label: '在线上限', children: `${value.license.maxConcurrentSessions} 个会话` },
        { key: 'features', label: '功能', span: 2, children: value.license.features.length ? value.license.features.map(item => <Tag key={item}>{item}</Tag>) : '无附加功能' },
      ]} />
      <div className="commerce-detail-actions"><Button type="primary" disabled={value.license.isTrial || !!value.license.frozenAt} onClick={() => open('renewal')}>申请续期</Button><Button onClick={() => detail.refetch()}>刷新详情</Button></div>
      <section className="commerce-detail-section"><h3>我的设备</h3><Table rowKey="id" dataSource={value.bindings} pagination={false} locale={{ emptyText: emptyTable }} scroll={{ x: 560 }} columns={[
        { title: '设备', dataIndex: 'deviceLabel', render: label => label || '未命名设备' }, { title: '绑定时间', dataIndex: 'boundAt', width: 180, render: dateText },
        { title: '状态', dataIndex: 'releasedAt', width: 95, render: released => released ? '已解绑' : '已绑定' },
        { title: '操作', fixed: 'right', width: 105, render: (_, binding) => <Button type="link" disabled={!!binding.releasedAt} onClick={() => open('rebinding', binding.id)}>申请换绑</Button> },
      ]} /></section>
    </>}
  </Drawer>
    <Modal title={draft?.type === 'rebinding' ? '申请换绑' : '申请续期'} open={!!draft} onCancel={() => { if (!busy) setDraft(null); }} closable={{ 'aria-label': '关闭', disabled: busy }} destroyOnHidden okText="提交申请" cancelText="取消" cancelButtonProps={{ disabled: busy }} maskClosable={false} confirmLoading={busy} okButtonProps={{ htmlType: 'submit', form: 'portal-request-form' }}>
      <p className="commerce-description">{draft?.type === 'rebinding' ? '申请批准后会解绑所选设备，并结束该设备的当前会话。换绑仍受次数和冷却限制。' : '向商家说明续期需求。时长与金额由商家核对后处理，提交申请不会立即改变授权。'}</p>
      {error && <Alert type="error" showIcon title={error} className="query-error" />}
      <Form id="portal-request-form" name="portal-request" form={form} layout="vertical" onFinish={submit} disabled={busy}><Form.Item label="申请原因" name="note" rules={[{ required: true, whitespace: true, message: '请说明申请原因或需求' }, { max: 500 }]}><Input.TextArea maxLength={500} showCount rows={4} /></Form.Item></Form>
    </Modal>
  </>;
}
