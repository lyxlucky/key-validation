import { Form, Select } from 'antd';

export function FeatureField({ label, name = 'features' }: { label: string; name?: string }) {
  return <Form.Item label={label} name={name}
    getValueFromEvent={(values: string[]) => [...new Set(values.map(value => value.trim().toLowerCase()))]}
    rules={[{ validator: async (_, values?: string[]) => {
      if ((values?.length ?? 0) > 50 || values?.some(value => !/^[a-z][a-z0-9._-]{0,63}$/.test(value))) {
        throw new Error('最多 50 项，每项以字母开头，可含小写字母、数字、点、下划线和短横线，最长 64 位');
      }
    } }]}
    extra="输入功能标识后按 Enter，例如 export、reports。留空表示不授予细分功能。">
    <Select mode="tags" tokenSeparators={[',', '，']} maxTagCount="responsive" placeholder="输入功能标识" />
  </Form.Item>;
}
