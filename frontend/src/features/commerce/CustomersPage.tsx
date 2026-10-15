import { useMemo, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { App, Button, Card, Grid, Input, Select, Table, Tag } from 'antd';
import { PlusOutlined, ReloadOutlined } from '@ant-design/icons';
import { api, queryString } from '../../lib/api';
import type { Page } from '../../lib/types';
import type { Channel, Customer, CustomerAccessCode } from '../../lib/commerce-types';
import { dateText, emptyTable, PageTitle, QueryError } from '../../components/common';
import { SecretDialog } from '../../components/SecretDialog';
import { useAuth } from '../auth/AuthProvider';
import { useOperationsCatalog } from '../operations/useOperationsCatalog';
import { CustomerEditor } from './CustomerEditor';
import type { CustomerFormValues } from './CustomerEditor';
import { CustomerDetail } from './CustomerDetail';
import { allPages, RowActions } from './commerce-shared';
import './commerce.css';

export function CustomersPage() {
  const auth = useAuth();
  const cache = useQueryClient();
  const { modal, message } = App.useApp();
  const screens = Grid.useBreakpoint();
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [applicationId, setApplicationId] = useState<string>();
  const [channelId, setChannelId] = useState<string>();
  const [editor, setEditor] = useState<{ customer: Customer | null; requestId: string } | null>(null);
  const [selected, setSelected] = useState<string | null>(null);
  const [secret, setSecret] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const initial = useMemo(() => editor?.customer ? { ...editor.customer, name: editor.customer.name ?? '', email: editor.customer.email ?? '', phone: editor.customer.phone ?? '', note: editor.customer.note ?? '' } : null, [editor]);
  const catalog = useOperationsCatalog();
  const channels = useQuery({ queryKey: ['channels', 'all'], queryFn: () => allPages<Channel>('/admin/channels'), enabled: auth.isAdmin });
  const customers = useQuery({ queryKey: ['customers', page, search, applicationId, channelId], queryFn: () => api<Page<Customer>>('/admin/customers' + queryString({ page, pageSize: 20, search: search.trim(), applicationId, channelId })) });
  async function refresh() { await cache.invalidateQueries({ predicate: query => ['customers', 'customer', 'order-catalog', 'audit'].includes(String(query.queryKey[0])) }); }
  async function save(values: CustomerFormValues) {
    if (!editor || busy) return;
    setBusy(true); setError(null);
    try {
      const customer = editor.customer;
      await api(customer ? `/admin/customers/${customer.id}` : '/admin/customers', { method: customer ? 'PATCH' : 'POST', body: customer
        ? { name: values.name.trim(), email: values.email?.trim() || null, phone: values.phone?.trim() || null, note: values.note?.trim() || null }
        : { ...values, name: values.name.trim(), channelId: values.channelId || null, requestId: editor.requestId } });
      setEditor(null); setPage(1); await refresh(); message.success('客户资料已保存');
    } catch (caught) { setError((caught as Error).message); }
    finally { setBusy(false); }
  }
  function changeState(customer: Customer, action: 'enable' | 'disable' | 'anonymize') {
    const label = { enable: '恢复客户', disable: '停用客户', anonymize: '匿名化客户' }[action];
    modal.confirm({ title: `${label}「${customer.name || '已匿名化客户'}」`, content: action === 'anonymize' ? '清除姓名、联系方式和备注，并撤销客户自助访问。订单与授权关系保留，已清除的个人资料无法恢复。' : action === 'disable' ? '停止该客户的新业务并撤销自助会话，已有授权按原规则保留。' : '恢复客户后可继续新业务；原自助会话不会恢复。', okText: `确认${label}`, cancelText: '取消', okButtonProps: { danger: action !== 'enable' }, onOk: async () => {
      try { await api(`/admin/customers/${customer.id}/${action}`, { method: 'POST' }); await refresh(); }
      catch (caught) { message.error((caught as Error).message); throw caught; }
    } });
  }
  function rotate(customer: Customer) {
    modal.confirm({ title: '生成客户访问码', content: `为「${customer.name}」生成新的访问码，旧访问码和已有自助会话立即失效。新码只在生成后显示，请通过已核实的渠道交付客户。`, okText: '生成新访问码', cancelText: '取消', onOk: async () => {
      try { const result = await api<CustomerAccessCode>(`/admin/customers/${customer.id}/rotate-access-code`, { method: 'POST' }); setSecret(result.accessCode); await refresh(); }
      catch (caught) { message.error((caught as Error).message); throw caught; }
    } });
  }
  return <><PageTitle title="客户管理" subtitle="维护客户资料，查看关联授权，并管理客户自助访问。" extra={<Button type="primary" icon={<PlusOutlined aria-hidden />} onClick={() => { setError(null); setEditor({ customer: null, requestId: crypto.randomUUID() }); }}>新建客户</Button>} />
    <QueryError error={customers.error} retry={customers.refetch} /><QueryError error={catalog.error} retry={catalog.refetch} /><QueryError error={channels.error} retry={channels.refetch} />
    <Card className="data-panel"><div className="panel-heading"><div><h2 className="panel-title">客户档案 <span className="count-badge">{customers.data?.total ?? '—'}</span></h2><p className="panel-description">客户资料按所属应用和渠道隔离。</p></div><Button icon={<ReloadOutlined aria-hidden />} onClick={() => customers.refetch()}>刷新</Button></div>
      <div className="filter-bar operations-filter">
        <div className="filter-field"><label htmlFor="customer-search">搜索客户</label><Input id="customer-search" placeholder="输入客户名称" allowClear value={search} maxLength={120} onChange={event => { setSearch(event.target.value); setPage(1); }} /></div>
        <div className="filter-field"><label htmlFor="customer-application-filter">所属应用</label><Select id="customer-application-filter" allowClear showSearch={{ optionFilterProp: 'label' }} placeholder="全部应用" value={applicationId} options={catalog.data?.applications.map(item => ({ value: item.id, label: item.name }))} onChange={value => { setApplicationId(value); setPage(1); }} /></div>
        {auth.isAdmin && <div className="filter-field"><label htmlFor="customer-channel-filter">所属渠道</label><Select id="customer-channel-filter" allowClear showSearch={{ optionFilterProp: 'label' }} placeholder="全部渠道" value={channelId} options={channels.data?.map(item => ({ value: item.id, label: item.name }))} onChange={value => { setChannelId(value); setPage(1); }} /></div>}
      </div>
      <Table<Customer> rowKey="id" dataSource={customers.data?.items} loading={customers.isLoading} locale={{ emptyText: emptyTable }} scroll={{ x: 1040 }} pagination={{ current: page, pageSize: 20, total: customers.data?.total, onChange: setPage, showSizeChanger: false, showTotal: total => `共 ${total} 位客户` }} columns={[
        { title: '客户名称', dataIndex: 'name', width: 190, render: (value, customer) => <div className="commerce-object"><strong>{value || '已匿名化客户'}</strong>{customer.note && <small>{customer.note}</small>}</div> },
        { title: '应用', dataIndex: 'applicationId', width: 155, render: id => catalog.data?.applications.find(item => item.id === id)?.name || '应用未记录' },
        { title: '渠道', dataIndex: 'channelId', width: 140, render: id => id ? channels.data?.find(item => item.id === id)?.name || '所属渠道' : '平台直营' },
        { title: '状态', width: 120, render: (_, customer) => <Tag color={customer.anonymizedAt ? 'default' : customer.disabledAt ? 'error' : 'success'}>{customer.anonymizedAt ? '已匿名化' : customer.disabledAt ? '已停用' : '已启用'}</Tag> },
        { title: '建档时间', dataIndex: 'createdAt', width: 170, render: dateText },
        { title: '操作', fixed: 'right', width: screens.md ? 320 : 96, render: (_, customer) => <RowActions items={[
          { key: 'detail', label: '详情', onClick: () => setSelected(customer.id) },
          { key: 'edit', label: '编辑', disabled: !!customer.anonymizedAt, onClick: () => { setError(null); setEditor({ customer, requestId: crypto.randomUUID() }); } },
          { key: 'access', label: '访问码', disabled: !!customer.disabledAt || !!customer.anonymizedAt, onClick: () => rotate(customer) },
          { key: 'state', label: customer.disabledAt ? '恢复' : '停用', disabled: !!customer.anonymizedAt, danger: !customer.disabledAt, onClick: () => changeState(customer, customer.disabledAt ? 'enable' : 'disable') },
          { key: 'anonymize', label: '匿名化', disabled: !!customer.anonymizedAt, danger: true, onClick: () => changeState(customer, 'anonymize') },
        ]} /> },
      ]} />
    </Card>
    <CustomerEditor open={!!editor} initial={initial} applications={catalog.data?.applications.map(item => ({ value: item.id, label: item.name })) ?? []} channels={channels.data?.map(item => ({ value: item.id, label: item.name, applicationIds: item.applicationIds })) ?? []} canChooseChannel={auth.isAdmin} busy={busy} error={error} save={save} close={() => setEditor(null)} />
    <CustomerDetail id={selected} close={() => setSelected(null)} />
    <SecretDialog title="保存客户访问码" value={secret} close={() => setSecret(null)} description={`客户可在 ${window.location.origin}/portal 输入此码进入自助服务。访问码只展示一次，请通过已核实的渠道交付；遗失后需要重新生成。`} />
  </>;
}
