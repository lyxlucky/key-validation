import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert, App, Button, Form, Input, Modal, Table } from 'antd';
import { PauseCircleOutlined } from '@ant-design/icons';
import { api } from '../../lib/api';
import type { Dto, LicenseDetail, Page } from '../../lib/types';
import { dateText, durationText, emptyTable, QueryError } from '../../components/common';
import { useAuth } from '../auth/AuthProvider';

export function FreezePolicyPanel({ license }: { license: NonNullable<LicenseDetail['license']> }) {
  const [page, setPage] = useState(1);
  const [operation, setOperation] = useState<'freeze' | 'unfreeze' | null>(null);
  const [saving, setSaving] = useState(false);
  const [form] = Form.useForm<{ reason: string }>();
  const { message } = App.useApp();
  const { canManage } = useAuth();
  const cache = useQueryClient();
  const history = useQuery({ queryKey: ['freeze-history', license.id, page], queryFn: () => api<Page<Dto<'LicenseFreezeDto'>>>(`/admin/licenses/${license.id}/freeze-history?page=${page}&pageSize=10`) });
  const frozen = !!license.frozenAt;
  return <section className="policy-section">
    <div className="panel-heading"><div><h3 className="section-heading"><PauseCircleOutlined /> 冻结与解冻</h3><p className="panel-description">冻结暂停授权计时，解冻后需要客户端重新激活。</p></div>
      {canManage && <Button disabled={!frozen && license.status !== 'active'} onClick={() => { form.resetFields(); setOperation(frozen ? 'unfreeze' : 'freeze'); }}>{frozen ? '解冻授权' : '冻结授权'}</Button>}
    </div>
    {frozen && <Alert className="query-error" type="info" showIcon title="授权计时已暂停" description={`冻结时剩余 ${durationText(Math.ceil(Number(license.frozenRemainingSeconds ?? 0)))}，冻结期间不能续期。${license.disabledAt ? '此卡同时处于禁用状态，解冻不会解除禁用。' : ''}`} />}
    <QueryError error={history.error} retry={history.refetch} />
    <Table<Dto<'LicenseFreezeDto'>> size="small" rowKey="id" loading={history.isLoading} dataSource={history.data?.items} locale={{ emptyText: emptyTable }} scroll={{ x: 780 }}
      pagination={{ current: page, pageSize: 10, total: history.data?.total, showSizeChanger: false, onChange: setPage, showTotal: total => `共 ${total} 次冻结` }} columns={[
        { title: '冻结时间', dataIndex: 'frozenAt', render: dateText },
        { title: '冻结原因', dataIndex: 'freezeReason', width: 180, render: value => <span className="break-word">{value}</span> },
        { title: '解冻时间', dataIndex: 'unfrozenAt', render: value => value ? dateText(value) : '尚未解冻' },
        { title: '解冻原因', dataIndex: 'unfreezeReason', width: 180, render: value => <span className="break-word">{value || '—'}</span> },
        { title: '操作人', width: 170, render: (_, record) => <div className="break-word"><div>冻结：{record.frozenBy}</div>{record.unfrozenBy && <div>解冻：{record.unfrozenBy}</div>}</div> },
      ]} />
    <Modal title={operation === 'unfreeze' ? '解冻授权' : '冻结授权'} open={!!operation} closable={{ 'aria-label': '关闭', disabled: saving }} onCancel={() => { if (!saving) setOperation(null); }}
      okText={operation === 'unfreeze' ? '确认解冻' : '确认冻结'} cancelText="取消" cancelButtonProps={{ disabled: saving }} maskClosable={false} confirmLoading={saving} onOk={() => form.submit()}>
      <p className="panel-description">{operation === 'unfreeze' ? '从现在起恢复冻结时的剩余可用时间。旧会话保持失效，禁用状态不会改变。' : '立即撤销全部现有会话并暂停授权计时，直到人工解冻。'}</p>
      <Form name="license-freeze-operation" form={form} layout="vertical" requiredMark={false} disabled={saving} onFinish={async values => {
        if (!operation || saving) return;
        setSaving(true);
        try {
          await api(`/admin/licenses/${license.id}/${operation}`, { method: 'POST', body: { reason: values.reason.trim() } });
          await cache.invalidateQueries(); setPage(1); setOperation(null); message.success('授权状态已更新');
        } catch (error) { message.error((error as Error).message); } finally { setSaving(false); }
      }}><Form.Item label="操作原因" name="reason" rules={[{ required: true, whitespace: true, message: '请填写操作原因' }]}><Input.TextArea rows={3} maxLength={500} showCount /></Form.Item></Form>
    </Modal>
  </section>;
}
