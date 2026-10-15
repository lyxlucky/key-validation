import { useApplicationPlans, invalidatePlanQueries } from '../plans/queries';
import { planOptions } from '../plans/options';
import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { Alert, App, Button, Form, Input, InputNumber, Select, Space, Table, Typography } from 'antd';
import { CheckCircleOutlined, CopyOutlined, DownloadOutlined, KeyOutlined } from '@ant-design/icons';
import { api, ApiError, queryString } from '../../lib/api';
import type { Application, Batch, BatchRequest, Issuance, Page } from '../../lib/types';
import { useAuth } from '../auth/AuthProvider';
import { useFormSnapshot } from '../../components/useFormSnapshot';
import { LiveModal } from '../../components/LiveModal';
import { CopyValue, copyText } from '../../components/CopyValue';
import { downloadText } from '../../lib/download';
import { durationText, QueryError } from '../../components/common';

function IssuanceSteps({ complete = false }: { complete?: boolean }) {
  return <ol className="issuance-steps" aria-label="发行步骤"><li className={complete ? 'is-complete' : 'is-current'} aria-current={!complete ? 'step' : undefined}><span>{complete ? <CheckCircleOutlined /> : '01'}</span>配置发行</li><li className={complete ? 'is-current' : ''} aria-current={complete ? 'step' : undefined}><span>02</span>保存结果</li></ol>;
}

function readPending(storageKey: string): BatchRequest | null {
  try {
    const raw = sessionStorage.getItem(storageKey);
    if (!raw) return null;
    const value = JSON.parse(raw) as Partial<BatchRequest>;
    return typeof value.applicationId === 'string' && typeof value.planId === 'string' && typeof value.requestId === 'string'
      && typeof value.quantity === 'number' && Number.isInteger(value.quantity) && value.quantity >= 1 && value.quantity <= 1000
      ? value as BatchRequest : null;
  } catch { return null; }
}
function removePending(storageKey: string) {
  try { sessionStorage.removeItem(storageKey); }
  catch { /* Storage cleanup must never discard an issuance response or block recovery. */ }
}
export function IssueModal({ open, close, applications, applicationsLoading = false, applicationsError = null, retryApplications }: {
  open: boolean; close: () => void; applications: Application[];
  applicationsLoading?: boolean; applicationsError?: Error | null; retryApplications?: () => unknown;
}) {
  const { user } = useAuth();
  const pendingKey = `keygate.pendingBatch.${user!.id}`;
  const [form] = Form.useForm();
  const appId = Form.useWatch('applicationId', form) as string | undefined;
  const planId = Form.useWatch('planId', form) as string | undefined;
  const [pending, setPending] = useState<BatchRequest | null>(() => readPending(pendingKey));
  const [recovered, setRecovered] = useState<Batch | null>(null);
  const [issued, setIssued] = useState<Issuance | null>(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const resultHeading = useRef<HTMLHeadingElement>(null);
  useLayoutEffect(() => {
    if (!issued) return;
    resultHeading.current?.closest('.ant-modal-body')?.scrollTo({ top: 0 });
    resultHeading.current?.focus({ preventScroll: true });
  }, [issued]);
  const { message, modal } = App.useApp();
  const cache = useQueryClient();
  const plans = useApplicationPlans(appId, open);
  useEffect(() => { if (!open) setError(null); }, [open]);
  useEffect(() => {
    if (!issued || saved) return;
    const warn = (event: BeforeUnloadEvent) => { event.preventDefault(); event.returnValue = ''; };
    window.addEventListener('beforeunload', warn);
    return () => window.removeEventListener('beforeunload', warn);
  }, [issued, saved]);
  useFormSnapshot(form, pending, open, () => pending!);
  async function checkPending() {
    if (!pending) return;
    setSaving(true);
    try {
      const result = await api<Page<Batch>>('/admin/license-batches' + queryString({ requestId: pending.requestId }));
      setRecovered(result.items[0] ?? null);
      if (!result.items.length) message.info('尚未找到已提交批次，可使用原请求重试。若上次请求仍在处理，重试不会重复发卡。');
    } catch (e) { message.error((e as Error).message); } finally { setSaving(false); }
  }
  const submit = async (values: Omit<BatchRequest, 'requestId'>) => {
    if (saving || issued) return;
    if (!pending && (plans.isPending || plans.isError)) return;
    const hadPendingRequest = pending !== null;
    const request: BatchRequest = pending ?? { ...values, requestId: crypto.randomUUID() };
    try { sessionStorage.setItem(pendingKey, JSON.stringify(request)); }
    catch { setError('无法保存待确认请求，请允许当前页面使用会话存储后重试。操作尚未发送。'); return; }
    setPending(request);
    setSaving(true); setError(null);
    try {
      const result = await api<Issuance>('/admin/license-batches', { method: 'POST', body: request });
      removePending(pendingKey); setPending(null); setRecovered(null); setSaved(false); setIssued(result);
      form.resetFields();
      void Promise.all(['licenses', 'batches', 'dashboard', 'operations-report', 'audit'].map(root => cache.invalidateQueries({ queryKey: [root] })));
    } catch (e) {
      setError((e as Error).message);
      // A first attempt rejected before commit is safe to edit. A recovery
      // retry can be rejected before the server checks the earlier attempt,
      // so that rejection cannot prove the original batch was never committed.
      if (!hadPendingRequest && e instanceof ApiError && ([400, 401, 403, 404, 429].includes(e.status) || e.code === 'APPLICATION_PLAN_DISABLED')) {
        removePending(pendingKey); setPending(null); setRecovered(null);
        if (e.code === 'PLAN_DISABLED' || e.code === 'APPLICATION_PLAN_DISABLED') {
          form.setFieldValue('planId', undefined);
          await invalidatePlanQueries(cache);
        }
      }
    } finally { setSaving(false); }
  };
  const disableRecovered = () => modal.confirm({ title: '禁用此批次并重新开始？', content: '这会禁用该批次全部卡密并撤销会话。随后可生成新批次。', okText: '确认禁用批次', cancelText: '取消', okButtonProps: { danger: true }, onOk: async () => {
    try { await api(`/admin/license-batches/${recovered!.id}/disable`, { method: 'POST' }); removePending(pendingKey); setPending(null); setRecovered(null); setError(null); await cache.invalidateQueries(); message.success('原批次已禁用，可重新生成'); } catch (e) { message.error((e as Error).message); throw e; }
  } });
  function download() {
    if (!issued) return;
    const csv = '\uFEFF卡密,卡密ID\r\n' + (issued.licenses ?? []).map(x => `${x.key},${x.id}`).join('\r\n');
    downloadText(csv, `keygate-${issued.batch?.id}.csv`, 'text/csv;charset=utf-8');
    setSaved(true);
  }
  function finish() { setIssued(null); close(); }
  function finishResult() {
    if (saved) return finish();
    modal.confirm({ title: '尚未复制或下载卡密', content: '关闭后无法再次获取本批次卡密明文。请先复制或下载保存。', okText: '返回保存', cancelText: '仍然关闭', onCancel: finish, maskClosable: false, keyboard: false, closable: false });
  }
  const selectedPlan = plans.data?.find(item => item.id === planId);
  return <LiveModal title={issued ? '卡密已生成' : '生成卡密'} open={open || !!issued}
    className="issuance-modal" data-testid="issuance-dialog" closable={issued ? false : { 'aria-label': '关闭', disabled: saving }} width={760}
    onCancel={() => { if (!saving && !issued) close(); }} maskClosable={false} keyboard={!issued && !saving}
    okText={pending ? '使用原请求重试' : '确认生成'} cancelText="取消" confirmLoading={saving}
    cancelButtonProps={{ disabled: saving }} okButtonProps={{ disabled: !!recovered || (!pending && !!plans.error) }} onOk={() => form.submit()}
    footer={issued ? <Space wrap><Button type="primary" icon={<DownloadOutlined aria-hidden />} onClick={download}>下载 CSV</Button><Button icon={<CopyOutlined aria-hidden />} onClick={async () => {
      try { await copyText(issued.licenses.map(item => item.key).join('\n')); setSaved(true); message.success('全部卡密已复制，请粘贴到安全位置保存'); }
      catch { message.error('复制失败，请下载 CSV 保存'); }
    }}>复制全部</Button><Button onClick={finishResult}>已保存，关闭</Button></Space> : undefined}>
    {(open || issued) && (issued ? <div key="result" className="issuance-stage" aria-live="polite">
    <IssuanceSteps complete />
    <div className="issuance-result-heading"><span className="issuance-success-icon"><CheckCircleOutlined /></span><div><h3 ref={resultHeading} tabIndex={-1}>已生成 {issued.batch?.quantity ?? issued.licenses.length} 张卡密</h3><p>请在关闭窗口前下载并保存本次发行结果。</p></div></div>
    <div className="issuance-batch"><span>发行批次</span><CopyValue value={issued.batch?.id} label="批次 ID" /></div>
    <Alert showIcon type="warning" title="卡密仅在此显示一次" description="请下载并妥善保存。关闭此窗口后，服务端无法再次提供明文。" className="query-error" />
    {saved && <Alert type="success" showIcon title="已复制或已发起下载，请确认卡密已妥善保存。" className="query-error" />}
    <Table rowKey="id" size="small" dataSource={issued.licenses} scroll={{ x: 520, y: 300 }} pagination={false} columns={[{ title: '卡密', dataIndex: 'key', render: value => <Typography.Text code copyable={{ text: value, tooltips: ['复制卡密', '已复制'], onCopy: () => { if (issued.licenses.length === 1) setSaved(true); } }}><span data-testid="issued-key">{value}</span></Typography.Text> }]} />
    </div> : <div key="configuration" className="issuance-stage issuance-config">
    <IssuanceSteps />
    <div className="overlay-intro"><span className="overlay-icon"><KeyOutlined /></span><div><strong>批量签发软件授权</strong><p>选择应用和卡种，设置本次发行的数量。</p></div></div>
    <Alert type="info" showIcon title="首次激活开始计时，生成后请立即保存卡密。" className="query-error" />
    <QueryError error={applicationsError} retry={retryApplications ?? (() => {})} />
    {!applicationsLoading && !applicationsError && !applications.some(app => !app.disabledAt) && <Alert type="warning" showIcon title="暂无可用应用" description="请先在应用管理中创建或恢复应用，并在应用设置中引用卡种。" className="query-error" />}
    {error && <Alert type="error" title={error} showIcon className="query-error" />}
    {pending && <Alert type="warning" showIcon title="存在待确认的发卡请求" description={<><p>原请求标识：<code className="break-word">{pending.requestId}</code></p><Space wrap><Button size="small" onClick={checkPending} loading={saving}>检查已创建批次</Button>{recovered && <Button size="small" danger onClick={disableRecovered}>禁用原批次</Button>}</Space>{recovered && <p>找到批次 <code className="break-word">{recovered.id}</code>，共 {recovered.quantity} 张。明文无法恢复。</p>}</>} className="query-error issuance-recovery" />}
    <Form name="issue-licenses" form={form} layout="vertical" requiredMark={false} initialValues={{ quantity: 10 }} onFinish={submit} disabled={!!pending || saving}>
      <div className="form-grid">
      <Form.Item label="所属应用" name="applicationId" rules={[{ required: true, message: '请选择应用' }]}><Select loading={applicationsLoading} disabled={applicationsLoading || !!applicationsError || !!pending || saving} showSearch={{ optionFilterProp: 'searchText' }} placeholder="搜索应用名称或标识" notFoundContent="暂无匹配的可用应用" options={applications.filter(x => !x.disabledAt).map(x => ({ value: x.id, label: x.name, code: x.code, searchText: `${x.name} ${x.code}` }))} optionRender={option => <div>{option.label}<div className="muted">{option.data.code}</div></div>} onChange={() => form.setFieldValue('planId', undefined)} /></Form.Item>
      <Form.Item label="卡种" name="planId" rules={[{ required: true, message: '请选择卡种' }]} extra={plans.isSuccess && appId && !plans.data?.some(x => x.isEnabled) ? '该应用暂无可发行卡种，请在应用设置中引用卡种并启用发行；新卡种在卡种管理中创建。' : undefined}><Select showSearch={{ optionFilterProp: 'label' }} loading={plans.isLoading} placeholder={appId ? '搜索并选择卡种' : '请先选择应用'} notFoundContent="暂无匹配的可发行卡种" options={planOptions(plans.data, appId, pending ? 'history' : 'issue')} disabled={!appId || plans.isLoading || !!plans.error || !!pending || saving} /></Form.Item>
      </div>
      {selectedPlan && <p className="form-note">本次授权：{durationText(selectedPlan.durationSeconds)} · {selectedPlan.maxDevices} 台设备 · {selectedPlan.maxConcurrentSessions} 个在线会话 · {selectedPlan.activationValidityDays ? `发行后 ${selectedPlan.activationValidityDays} 天内激活` : '激活期限不限'}</p>}
      {plans.error && <Alert type="error" showIcon title={plans.error.message} className="query-error" action={<Button size="small" onClick={() => plans.refetch()}>重试</Button>} />}
      <Form.Item label="生成数量" name="quantity" rules={[{ required: true, message: '请输入数量' }]} extra="每次可生成 1–1000 张卡密。"><InputNumber min={1} max={1000} precision={0} style={{ width: '100%' }} /></Form.Item>
      <Form.Item label="批次备注" name="note"><Input.TextArea rows={2} maxLength={500} showCount placeholder="可选，例如：十月渠道发行" /></Form.Item>
    </Form>
    </div>)}
  </LiveModal>;
}

