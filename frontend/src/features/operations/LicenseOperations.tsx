import { createContext, useContext, useEffect, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { Alert, App, Button, Form, Input, InputNumber, Modal, Select, Space, Table, Tag } from 'antd';
import { useQueryClient } from '@tanstack/react-query';
import { api, ApiError, errorCodeText } from '../../lib/api';
import type { License } from '../../lib/types';
import type { LicenseAction, OperationRequest, OperationResult } from '../../lib/management-types';
import { dateText, LicenseStatus } from '../../components/common';
import { useAuth } from '../auth/AuthProvider';

type Target = Pick<License, 'id' | 'keyHint' | 'status' | 'note' | 'tags'>;
interface OperationContext { openOperation: (action: LicenseAction, targets: Target[]) => void }
const Context = createContext<OperationContext | null>(null);
interface RecoveryState {
  storageKey: string;
  pending: OperationRequest | null;
  setPending: (request: OperationRequest | null) => void;
  busy: boolean;
  setBusy: (busy: boolean) => void;
  busyRef: { current: boolean };
  resumeRequest: { tabId: number; sequence: number } | null;
  resume: () => void;
}
const RecoveryContext = createContext<RecoveryState | null>(null);
const actionNames: Record<LicenseAction, string> = { disable: '禁用', enable: '恢复', extend: '续期', metadata: '编辑备注标签' };
const impacts: Record<LicenseAction, string> = {
  disable: '所选卡密将被禁用，所有现有会话会被撤销，客户端不能继续激活或续租。',
  enable: '恢复卡密不会延长有效期，也不会恢复旧会话；客户端需要重新激活。应用状态保持不变。',
  extend: '未激活卡密增加待激活时长；未过期卡密从原到期时间累加；已过期卡密从服务端当前时间起算。禁用卡不会自动恢复，设备绑定保留，旧的失效或撤销会话不会恢复。',
  metadata: '本次操作会整体替换所选卡密的备注和标签。留空表示清空，不会追加到原有内容。',
};

function restorePending(storageKey: string): OperationRequest | null {
  try {
    const raw = sessionStorage.getItem(storageKey);
    if (!raw) return null;
    const value = JSON.parse(raw) as Partial<OperationRequest>;
    if (typeof value.requestId !== 'string' || !Array.isArray(value.licenseIds) || value.licenseIds.length < 1 || value.licenseIds.length > 100 || value.licenseIds.some(id => typeof id !== 'string') || !['disable', 'enable', 'extend', 'metadata'].includes(value.action ?? '') || typeof value.reason !== 'string') return null;
    const request: OperationRequest = { requestId: value.requestId, licenseIds: [...value.licenseIds], action: value.action!, reason: value.reason };
    if (request.action === 'extend') {
      if (typeof value.additionalSeconds !== 'number' || !Number.isInteger(value.additionalSeconds) || value.additionalSeconds < 3600 || value.additionalSeconds % 3600 !== 0) return null;
      request.additionalSeconds = value.additionalSeconds;
    }
    if (request.action === 'metadata') {
      if (typeof value.note !== 'string' || !Array.isArray(value.tags) || value.tags.some(tag => typeof tag !== 'string')) return null;
      request.note = value.note; request.tags = [...value.tags];
    }
    return request;
  } catch { return null; }
}

// Only submitted, immutable requests are shared across the workspace. Each page
// owns its dialog and draft inside its Activity boundary.
export function LicenseOperationRecoveryProvider({ activeTabId, children }: { activeTabId?: number; children: ReactNode }) {
  const { user } = useAuth();
  const storageKey = `keygate.pendingOperation.${user!.id}`;
  const [pending, setPending] = useState<OperationRequest | null>(() => restorePending(storageKey));
  const [busy, setBusy] = useState(false);
  const busyRef = useRef(false);
  const [resumeRequest, setResumeRequest] = useState<RecoveryState['resumeRequest']>(null);
  function resume() {
    if (activeTabId === undefined) return;
    setResumeRequest(current => ({ tabId: activeTabId, sequence: (current?.sequence ?? 0) + 1 }));
  }
  return <RecoveryContext.Provider value={{ storageKey, pending, setPending, busy, setBusy, busyRef, resumeRequest, resume }}>{children}</RecoveryContext.Provider>;
}

function useOperationRecovery() {
  const context = useContext(RecoveryContext);
  if (!context) throw new Error('LicenseOperationRecoveryProvider missing');
  return context;
}

export function LicenseOperationsProvider({ tabId, children }: { tabId: number; children: ReactNode }) {
  const { canManage } = useAuth();
  const { storageKey, pending, setPending, busy, setBusy, busyRef, resumeRequest } = useOperationRecovery();
  const [open, setOpen] = useState(false);
  const [action, setAction] = useState<LicenseAction>('extend');
  const [targets, setTargets] = useState<Target[]>([]);
  const [result, setResult] = useState<OperationResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const resumedSequence = useRef(0);
  const [form] = Form.useForm();
  const cache = useQueryClient();
  const { message } = App.useApp();
  const displayedAction = pending?.action ?? result?.action ?? action;
  const count = pending?.licenseIds.length ?? targets.length;
  const title = count > 1 ? `批量${actionNames[displayedAction]}` : displayedAction === 'metadata' ? '编辑备注标签' : `${actionNames[displayedAction]}卡密`;
  useEffect(() => {
    if (resumeRequest?.tabId !== tabId || resumeRequest.sequence <= resumedSequence.current) return;
    resumedSequence.current = resumeRequest.sequence;
    setError(null); setResult(null); setOpen(true);
  }, [resumeRequest, tabId]);
  function resume() { setError(null); setResult(null); setOpen(true); }
  function openOperation(nextAction: LicenseAction, nextTargets: Target[]) {
    if (!canManage) return;
    if (pending) { resume(); return; }
    if (!nextTargets.length || nextTargets.length > 100) return;
    setAction(nextAction); setTargets(nextTargets.map(target => ({ ...target, tags: [...(target.tags ?? [])] }))); setResult(null); setError(null);
    form.resetFields();
    form.setFieldsValue({ hours: 24, note: nextTargets.length === 1 ? nextTargets[0].note ?? '' : '', tags: nextTargets.length === 1 ? nextTargets[0].tags ?? [] : [], reason: '' });
    setOpen(true);
  }
  async function acceptResult(receipt: OperationResult) {
    sessionStorage.removeItem(storageKey);
    setPending(null); setResult(receipt); setError(null);
    await cache.invalidateQueries({ predicate: query => ['licenses', 'license', 'dashboard', 'batches', 'batch', 'expiring', 'audit'].includes(String(query.queryKey[0])) });
  }
  async function submit(values?: { hours: number; note: string; tags: string[]; reason: string }) {
    if (busyRef.current || !canManage) return;
    const hadPending = pending !== null;
    let request = pending;
    if (!request) {
      if (!values) return;
      request = { requestId: crypto.randomUUID(), licenseIds: [...new Set(targets.map(target => target.id))], action, reason: values.reason.trim() };
      if (action === 'extend') request.additionalSeconds = values.hours * 3600;
      if (action === 'metadata') { request.note = (values.note ?? '').trim(); request.tags = [...new Set((values.tags ?? []).map(tag => tag.trim()).filter(Boolean))]; }
    }
    // Persist only this immutable request snapshot before sending anything to the server.
    try { sessionStorage.setItem(storageKey, JSON.stringify(request)); } catch { setError('无法保存待确认请求，请允许当前页面使用会话存储后重试。操作尚未发送。'); return; }
    setPending(request); setError(null); setBusy(true); busyRef.current = true;
    try { await acceptResult(await api<OperationResult>('/admin/license-operations', { method: 'POST', body: request })); }
    catch (caught) {
      setError(caught instanceof ApiError ? caught.message : '连接中断，操作结果尚未确认。请检查操作结果，或使用原请求重试。');
      // A later rejection cannot establish whether an earlier attempt committed.
      if (!hadPending && caught instanceof ApiError && [400, 401, 403, 404, 429].includes(caught.status)) { sessionStorage.removeItem(storageKey); setPending(null); }
    } finally { setBusy(false); busyRef.current = false; }
  }
  async function checkReceipt() {
    if (!pending || busyRef.current) return;
    setBusy(true); busyRef.current = true; setError(null);
    try { await acceptResult(await api<OperationResult>(`/admin/license-operations/${pending.requestId}`)); }
    catch (caught) { if (caught instanceof ApiError && caught.status === 404) message.info('暂未找到操作结果。原请求仍已保留，可使用原请求重试；不会重复续期。'); else setError((caught as Error).message); }
    finally { setBusy(false); busyRef.current = false; }
  }
  const preview = pending ? pending.licenseIds.map(id => ({ id, keyHint: targets.find(target => target.id === id)?.keyHint ?? id })) : targets;
  return <Context.Provider value={{ openOperation }}>{children}
    <Modal className="license-operation-modal" title={result ? '操作结果' : title} open={open} width={720} closable={{ 'aria-label': '关闭', disabled: busy }} maskClosable={false} onCancel={() => { if (!busy) setOpen(false); }} footer={result ? <Button type="primary" onClick={() => setOpen(false)}>完成</Button> : <Space wrap><Button disabled={busy} onClick={() => setOpen(false)}>取消</Button>{pending && <Button loading={busy} onClick={checkReceipt}>检查操作结果</Button>}<Button type="primary" danger={displayedAction === 'disable'} loading={busy} disabled={!canManage} onClick={() => pending ? submit() : form.submit()}>{pending ? '使用原请求重试' : '确认执行'}</Button></Space>}>
      {error && <Alert type="error" showIcon title={error} className="query-error" />}
      {result ? <><Alert type={result.failed ? 'warning' : 'success'} showIcon title={`成功 ${result.succeeded} 张，失败 ${result.failed} 张`} description={`完成时间：${dateText(result.completedAt)}`} className="query-error" /><Table rowKey="licenseId" size="small" pagination={false} scroll={{ x: 540, y: 300 }} dataSource={result.items} columns={[{ title: '卡密', render: (_, item) => <code className="break-word">{item.license?.keyHint ?? item.licenseId}</code> }, { title: '结果', render: (_, item) => item.success ? <Tag color="success">成功</Tag> : <Tag color="error">失败</Tag> }, { title: '说明', render: (_, item) => item.success ? item.license?.expiresAt ? `到期：${dateText(item.license.expiresAt)}` : '已更新' : errorCodeText(item.errorCode) }]} /></> : <>
        <Alert type={displayedAction === 'disable' ? 'warning' : 'info'} showIcon title={`将${actionNames[displayedAction]} ${count} 张卡密`} description={impacts[displayedAction]} className="query-error" />
        {pending && <Alert type="warning" showIcon title="存在待确认的卡密操作" description={<><p>原参数已锁定，检查结果或重试均使用同一个请求。</p><code className="break-word">{pending.requestId}</code><p>操作原因：{pending.reason}</p>{pending.action === 'extend' && <p>延长时长：{pending.additionalSeconds! / 3600} 小时</p>}{pending.action === 'metadata' && <><p>替换备注：{pending.note || '清空'}</p><p>替换标签：{pending.tags?.join('、') || '清空'}</p></>}</>} className="query-error" />}
        <Table rowKey="id" size="small" pagination={false} dataSource={preview} scroll={{ x: 430, y: 180 }} className="operation-preview" columns={[{ title: '所选卡密', dataIndex: 'keyHint', render: value => <code className="break-word">{value}</code> }, { title: '状态', render: (_, record) => 'status' in record ? <LicenseStatus status={String(record.status)} /> : '待确认' }]} />
        {!pending && <Form name={`license-operation-${tabId}`} form={form} layout="vertical" disabled={busy} requiredMark={false} onFinish={submit}>
          {action === 'extend' && <Form.Item label="延长时长（小时）" name="hours" rules={[{ required: true, message: '请输入延长时长' }, { type: 'integer', min: 1, max: 87600, message: '请输入 1–87600 的整数小时' }]} extra="每次 1–87600 小时；续期后的总时长或到期时间仍受十年上限约束。"><InputNumber min={1} max={87600} precision={0} style={{ width: '100%' }} /></Form.Item>}
          {action === 'metadata' && <><Form.Item label="卡密备注" name="note"><Input.TextArea maxLength={500} rows={3} showCount /></Form.Item><Form.Item label="标签" name="tags" rules={[{ validator: async (_, tags: string[] = []) => { const normalized = [...new Set(tags.map(tag => tag.trim()).filter(Boolean))]; if (normalized.length > 10 || normalized.some(tag => tag.length > 32)) throw new Error('最多 10 个标签，每个标签不超过 32 字'); } }]} extra="输入标签后按回车，最多 10 个。留空将清空标签。"><Select mode="tags" tokenSeparators={[',', '，']} options={[]} /></Form.Item></>}
          <Form.Item label="操作原因" name="reason" rules={[{ required: true, whitespace: true, message: '请填写操作原因' }, { max: 500 }]}><Input.TextArea maxLength={500} rows={2} showCount placeholder="例如：客户续费、渠道调整" /></Form.Item>
        </Form>}
      </>}
    </Modal>
  </Context.Provider>;
}

export function useLicenseOperations() { const context = useContext(Context); if (!context) throw new Error('LicenseOperationsProvider missing'); return context; }
export function PendingOperationNotice() {
  const { pending, resume } = useOperationRecovery();
  return pending ? <Alert className="query-error" type="warning" showIcon title="有一项卡密操作待确认" description="原请求已保存，可检查执行结果或安全重试。" action={<Button onClick={resume}>处理待确认操作</Button>} /> : null;
}
