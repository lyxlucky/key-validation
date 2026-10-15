# 前端项目

React + TypeScript + Vite，组件库使用 Ant Design，服务端状态使用 TanStack Query。

## 目录结构

```text
src/
  main.tsx                # 挂载 React 应用
  app/                    # 应用组装层
    AppProviders.tsx      # 主题、语言、路由 Provider
    ApplicationRoot.tsx   # 管理端/客户门户入口及会话缓存边界
    App.tsx               # 管理端登录与认证路由
    AdminLayout.tsx       # 管理端布局、导航及工作区组合
    navigation.tsx        # 页面、菜单与访问条件配置
    RequireAccess.tsx     # 页面访问权限检查
    theme.ts              # Ant Design 主题配置
    styles.ts             # 全局样式加载顺序
  features/               # 按业务域组织的页面、组件、查询与专属样式
  components/             # 跨业务复用的 UI 组件与表单辅助逻辑
  lib/                    # 请求客户端、共享类型和通用工具
  styles/                 # 全局基础样式及跨业务页面样式
e2e/                      # Playwright 回归测试及测试辅助工具
scripts/                  # 安装与开发辅助脚本
```

## 开发约定

- 新增业务页面放入对应的 `features/<业务域>/`，在 `app/navigation.tsx` 注册菜单、路径与访问条件。
- 业务私有组件、查询、类型和样式就近放在业务目录；多个业务实际复用后再提取到 `components/` 或 `lib/`。
- 应用级布局与 Provider 放在 `app/`。业务模块不依赖 `app/`，由应用层组合业务模块。
- 业务样式由所属模块导入；需要全局加载的样式统一在 `app/styles.ts` 中按顺序导入。
- 管理端与客户门户保持各自的会话查询边界，避免用户切换或跨端导航时复用上一会话缓存。

## 常用命令

在 `frontend` 目录执行：

```sh
npm ci
npm run dev
npm run build
npm run test:e2e
```

开发服务器默认使用 `127.0.0.1:5173`，后端代理默认指向 `127.0.0.1:5080`，可通过 `API_PROXY_TARGET` 配置。
部分端到端测试需要运行后端并设置 `E2E_ADMIN_USER`、`E2E_ADMIN_PASSWORD`；使用页面拦截数据的测试可独立运行。
