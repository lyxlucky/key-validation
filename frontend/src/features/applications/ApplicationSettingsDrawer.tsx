import { Drawer } from 'antd';
import type { Application } from '../../lib/types';
import { ApplicationPlanReferences } from '../plans/ApplicationPlanReferences';
import { TrialPolicyPanel } from './TrialPolicyPanel';
import { CopyValue } from '../../components/CopyValue';

export function ApplicationSettingsDrawer({ application, close }: {
  application: Application | null; close: () => void;
}) {
  return <Drawer title={(application?.name ?? '') + ' · 应用设置'} open={!!application}
    size={820} onClose={close} closable={{ 'aria-label': '关闭' }}>
    {application && <>
      <div className="detail-summary application-summary">
        <div><h3>{application.name}</h3><CopyValue value={application.code} label="应用标识" /></div>
      </div>
      <ApplicationPlanReferences key={application.id} application={application} />
      <TrialPolicyPanel key={'trial-' + application.id} applicationId={application.id} />
    </>}
  </Drawer>;
}
