import React from 'react';
import ReactDOM from 'react-dom/client';
import { ApplicationRoot } from './app/ApplicationRoot';
import { AppProviders } from './app/AppProviders';
import './app/styles';

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode><AppProviders><ApplicationRoot /></AppProviders></React.StrictMode>,
);
