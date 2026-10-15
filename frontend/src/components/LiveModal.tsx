import { useState } from 'react';
import { Modal } from 'antd';
import type { ModalProps } from 'antd';

// Keep closing children live until afterClose so cleared drafts and secrets
// disappear at the close commit. Then release the hidden portal: permanently
// forcing it to render would leave an invisible entry on dialog's Escape stack.
export function LiveModal({ open = false, afterClose, ...props }: Omit<ModalProps, 'forceRender' | 'destroyOnHidden'>) {
  const [rendered, setRendered] = useState(open);
  if (open && !rendered) setRendered(true);
  return <Modal {...props} cancelButtonProps={{ disabled: !!props.confirmLoading, ...props.cancelButtonProps }} open={open} forceRender={rendered} destroyOnHidden afterClose={() => { setRendered(false); afterClose?.(); }} />;
}
