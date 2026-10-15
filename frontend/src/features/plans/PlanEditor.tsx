import { useEffect, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { Alert, App, Form, Input, InputNumber, Select, Switch } from 'antd';
import { LiveModal } from '../../components/LiveModal';
import { useFormSnapshot } from '../../components/useFormSnapshot';
import { FeatureField } from '../policies/FeatureField';
import { savePlan } from './queries';
import type { GlobalPlan } from './types';

const durationUnits = [
  { value: 'minute', label: '分钟', seconds: 60 },
  { value: 'hour', label: '小时', seconds: 3600 },
  { value: 'day', label: '天', seconds: 86400 },
  { value: 'month', label: '月', seconds: 2592000 },
  { value: 'year', label: '年', seconds: 31536000 },
] as const;
type DurationUnit = typeof durationUnits[number]['value'];
const maxDurationSeconds = 315360000;

function durationFields(seconds: number) {
  const unit = [...durationUnits].reverse().find(unit => seconds % unit.seconds === 0)
    ?? durationUnits[0];
  return { durationValue: seconds / unit.seconds, durationUnit: unit.value };
}

interface Values {
  name: string; durationValue: number; durationUnit: DurationUnit; maxDevices: number;
  maxConcurrentSessions: number; activationValidityDays: number | null;
  features: string[]; isEnabled: boolean;
}

export function PlanEditor({ open, plan, close }: {
  open: boolean; plan: GlobalPlan | null; close: () => void;
}) {
  const [form] = Form.useForm<Values>();
  const selectedUnit = Form.useWatch('durationUnit', form) ?? 'month';
  const unitSeconds = durationUnits.find(unit => unit.value === selectedUnit)!.seconds;
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const cache = useQueryClient();
  const { message } = App.useApp();
  useEffect(() => { setError(null); }, [open, plan?.id]);
  useFormSnapshot(form, plan ?? 'new-plan', open, () => ({
    name: plan?.name ?? '', ...durationFields(plan ? Number(plan.durationSeconds) : 2592000),
    maxDevices: Number(plan?.maxDevices ?? 1),
    maxConcurrentSessions: Number(plan?.maxConcurrentSessions ?? 1),
    activationValidityDays: plan?.activationValidityDays == null
      ? null : Number(plan.activationValidityDays),
    features: plan?.features ?? [], isEnabled: plan?.isEnabled ?? true,
  }));
  async function save(values: Values) {
    if (saving) return;
    setSaving(true); setError(null);
    try {
      await savePlan(cache, plan?.id, {
        name: values.name.trim(), durationSeconds: Math.round(values.durationValue *
          durationUnits.find(unit => unit.value === values.durationUnit)!.seconds),
        maxDevices: values.maxDevices, maxConcurrentSessions: values.maxConcurrentSessions,
        activationValidityDays: values.activationValidityDays ?? null,
        features: values.features ?? [], isEnabled: values.isEnabled,
      });
      message.success('卡种已保存'); close();
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : '保存失败，请重试');
    } finally { setSaving(false); }
  }
  return <LiveModal className="policy-editor-modal" open={open}
    title={plan ? '编辑卡种' : '创建卡种'} onCancel={() => { if (!saving) close(); }}
    closable={{ 'aria-label': '关闭', disabled: saving }} cancelButtonProps={{ disabled: saving }} maskClosable={false} confirmLoading={saving}
    okText="保存卡种" cancelText="取消" onOk={() => form.submit()}>
    {error && <Alert type="error" showIcon title={error} />}
    <Form form={form} name="global-plan-editor" layout="vertical"
      requiredMark={false} onFinish={save} disabled={saving}>
      <Form.Item label="卡种名称" name="name"
        rules={[{ required: true, whitespace: true, message: '请输入卡种名称' }]}>
        <Input maxLength={120} placeholder="例如：月卡、年卡" />
      </Form.Item>
      <div className="form-grid">
        <Form.Item label="授权时长" name="durationValue" dependencies={['durationUnit']}
          rules={[{ required: true, message: '请输入授权时长' },
            { validator: (_, value) => {
              const seconds = Math.round(value * unitSeconds);
              return value != null && Number.isFinite(value) && value > 0 &&
                seconds >= 1 && seconds <= maxDurationSeconds
                ? Promise.resolve() : Promise.reject(new Error('授权时长须为 1 秒至 10 年'));
            } }]}>
          <InputNumber min={1 / unitSeconds} max={maxDurationSeconds / unitSeconds}
            style={{ width: '100%' }} />
        </Form.Item>
        <Form.Item label="时长单位" name="durationUnit"
          tooltip="1 天 = 24 小时，1 月 = 30 天，1 年 = 365 天。">
          <Select options={durationUnits.map(({ value, label }) => ({ value, label }))} />
        </Form.Item>
      </div>
      <div className="form-grid">
        <Form.Item label="设备上限" name="maxDevices" rules={[{ required: true }]}>
          <InputNumber min={1} max={100} precision={0} style={{ width: '100%' }} />
        </Form.Item>
        <Form.Item label="在线会话上限" name="maxConcurrentSessions"
          rules={[{ required: true, message: '请输入在线会话上限' }]}>
          <InputNumber min={1} max={100} precision={0} style={{ width: '100%' }} />
        </Form.Item>
      </div>
      <div className="form-grid">
        <Form.Item label="激活期限（天）" name="activationValidityDays"
          extra="从发行起计算，留空不限。">
          <InputNumber min={1} max={3650} precision={0} style={{ width: '100%' }} />
        </Form.Item>
      </div>
      <FeatureField label="授权功能" />
      {plan && <Form.Item label="允许继续发行" name="isEnabled" valuePropName="checked">
        <Switch />
      </Form.Item>}
      <p className="form-note">所有引用应用统一使用这些规则；已发卡密和已创建订单保留原参数。</p>
    </Form>
  </LiveModal>;
}
