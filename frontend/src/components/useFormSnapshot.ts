import { useEffect, useRef } from 'react';
import type { FormInstance } from 'antd';

// Activity reconnects effects when a tab becomes visible. Reconnect should not
// overwrite an unsaved draft with the same server snapshot a second time.
export function useFormSnapshot<T>(form: FormInstance<T>, snapshot: unknown, active: boolean, values: () => Parameters<FormInstance<NoInfer<T>>['setFieldsValue']>[0]) {
  const applied = useRef<unknown>(undefined);
  useEffect(() => {
    if (!active) { applied.current = undefined; return; }
    if (snapshot == null || applied.current === snapshot) return;
    applied.current = snapshot;
    form.setFieldsValue(values());
  }, [form, snapshot, active]);
}
