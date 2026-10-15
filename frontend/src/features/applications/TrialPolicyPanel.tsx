import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { App, Button, Descriptions, Form, InputNumber, Modal, Switch, Tag } from 'antd';
import { ExperimentOutlined } from '@ant-design/icons';
import { api } from '../../lib/api';
import type { Dto } from '../../lib/types';
import { durationText, QueryError } from '../../components/common';
import { useAuth } from '../auth/AuthProvider';
import { FeatureField } from '../policies/FeatureField';

type TrialForm = { isEnabled: boolean; durationHours: number; features: string[] };

export function TrialPolicyPanel({ applicationId }: { applicationId: string }) {
  const policy = useQuery({ queryKey: ['trial-policy', applicationId], queryFn: () => api<Dto<'TrialPolicyDto'>>(`/admin/applications/${applicationId}/trial-policy`) });
  const { isAdmin } = useAuth();
  const { message } = App.useApp();
  const cache = useQueryClient();
  const [open, setOpen] = useState(false);
  const [saving, setSaving] = useState(false);
  const [form] = Form.useForm<TrialForm>();
  const value = policy.data;
  return <section className="policy-section">
    <div className="panel-heading"><div><h3 className="section-heading"><ExperimentOutlined aria-hidden /> 试用授权</h3><p className="panel-description">每台设备在此应用可领取一次试用，使用正式卡后自动升级。</p></div>
      {isAdmin && <Button disabled={!value} onClick={() => {
        form.setFieldsValue({ isEnabled: value!.isEnabled, durationHours: Number(value!.durationSeconds) / 3600, features: value!.features ?? [] });
        setOpen(true);
      }}>配置试用</Button>}
    </div>
    <QueryError error={policy.error} retry={policy.refetch} />
    {value && <Descriptions size="small" column={1} items={[
      { key: 'enabled', label: '领取状态', children: <Tag color={value.isEnabled ? 'success' : 'default'}>{value.isEnabled ? '开放领取' : '未开放'}</Tag> },
      { key: 'duration', label: '试用时长', children: durationText(value.durationSeconds) },
      { key: 'features', label: '试用功能', children: <div className="license-tags">{value.features?.length ? value.features.map(feature => <Tag key={feature}>{feature}</Tag>) : '未配置细分功能'}</div> },
    ]} />}
    <Modal className="policy-editor-modal" title="试用授权配置" open={open} closable={{ 'aria-label': '关闭', disabled: saving }} onCancel={() => { if (!saving) setOpen(false); }} okText="保存试用规则" cancelText="取消" cancelButtonProps={{ disabled: saving }} maskClosable={false} confirmLoading={saving} onOk={() => form.submit()}>
      <p className="panel-description">修改只影响之后首次领取的设备；关闭领取不会提前结束已有试用。</p>
      <Form name="trial-policy-editor" form={form} layout="vertical" requiredMark={false} disabled={saving} onFinish={async values => {
        if (saving) return;
        setSaving(true);
        try {
          await api(`/admin/applications/${applicationId}/trial-policy`, { method: 'PUT', body: { isEnabled: values.isEnabled, durationSeconds: Math.round(values.durationHours * 3600), features: values.features ?? [] } });
          await Promise.all([cache.invalidateQueries({ queryKey: ['trial-policy', applicationId] }), cache.invalidateQueries({ queryKey: ['audit'] })]);
          setOpen(false); message.success('试用规则已保存');
        } catch (error) { message.error((error as Error).message); } finally { setSaving(false); }
      }}>
        <Form.Item label="允许领取试用" name="isEnabled" valuePropName="checked"><Switch aria-label="允许领取试用" /></Form.Item>
        <Form.Item label="试用时长（小时）" name="durationHours" rules={[{ required: true, message: '请输入试用时长' }]}><InputNumber min={1 / 3600} max={720} style={{ width: '100%' }} /></Form.Item>
        <FeatureField label="试用功能" />
      </Form>
    </Modal>
  </section>;
}

