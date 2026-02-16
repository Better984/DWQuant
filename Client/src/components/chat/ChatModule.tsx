import React, { useState } from 'react';
import './ChatModule.css';

type MessageRole = 'user' | 'assistant' | 'system';

interface ChatMessage {
  role: MessageRole;
  from: string;
  time: string;
  text: string;
  strategyBlock?: string;
}

const ChatModule: React.FC = () => {
  const [inputValue, setInputValue] = useState('');

  const messages: ChatMessage[] = [
    {
      role: 'system',
      from: '系统',
      time: '09:28',
      text: '你好，我是量化助手。可以和我讨论当前行情、技术面与情绪指标，也可以让我根据你的偏好生成可在本平台运行的策略配置。',
    },
    {
      role: 'user',
      from: '你',
      time: '09:30',
      text: '最近 BTC 在 6 万附近反复震荡，恐惧贪婪指数也在中性区，你觉得适合做趋势跟踪还是区间网格？',
    },
    {
      role: 'assistant',
      from: '量化助手',
      time: '09:31',
      text: '在震荡+情绪中性的环境下，纯趋势跟踪容易频繁假突破，区间策略或网格更贴合当前结构。可以优先考虑：\n\n1. 用 ATR 或近期高低点画出震荡区间，在区间下沿分批挂多、上沿减仓；\n2. 保留少量趋势仓位，用更长周期均线过滤，只在明确突破区间后再加仓。\n\n需要的话我可以按你指定的标的和参数，生成一份可直接导入本平台的策略配置。',
    },
    {
      role: 'user',
      from: '你',
      time: '09:33',
      text: '帮我生成一个 BTC 现货的简单网格策略，区间 58000–62000，5 格。',
    },
    {
      role: 'assistant',
      from: '量化助手',
      time: '09:34',
      text: '已根据你的区间与格数生成网格策略配置，可直接导入「策略」模块使用。',
      strategyBlock: '策略类型：现货网格\n标的：BTC/USDT\n区间下限：58000\n区间上限：62000\n网格数量：5\n每格投入比例：20%\n触发方式：限价挂单',
    },
  ];

  const handleSend = () => {
    const trimmed = inputValue.trim();
    if (!trimmed) return;
    // 当前为静态演示，不追加真实消息
    setInputValue('');
  };

  return (
    <div className="module-container chat-module-container">
      <div className="page-title">
        <h1 className="title-text">AI 助手</h1>
      </div>
      <div className="chat-module-panel">
        <div className="chat-messages">
          {messages.map((m, index) => (
            <div
              key={index}
              className={`chat-message is-${m.role}`}
            >
              <div className="chat-message-header">
                <span className="chat-message-from">{m.from}</span>
                <span className="chat-message-time">{m.time}</span>
              </div>
              <div className="chat-message-text">{m.text}</div>
              {m.strategyBlock && (
                <div className="chat-strategy-block">
                  <div className="chat-strategy-block-title">可导入策略摘要</div>
                  {m.strategyBlock}
                </div>
              )}
            </div>
          ))}
        </div>
        <div className="chat-input-wrap">
          <div className="chat-input-inner">
            <textarea
              className="chat-input"
              placeholder="输入问题：讨论行情或描述你想要的策略（如标的、区间、网格数等）…"
              value={inputValue}
              onChange={(e) => setInputValue(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter' && !e.shiftKey) {
                  e.preventDefault();
                  handleSend();
                }
              }}
              rows={1}
              aria-label="输入消息"
            />
            <button
              type="button"
              className="chat-send-btn"
              onClick={handleSend}
              disabled={!inputValue.trim()}
              aria-label="发送"
            >
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                <line x1="22" y1="2" x2="11" y2="13" />
                <polygon points="22 2 15 22 11 13 2 9 22 2" />
              </svg>
            </button>
          </div>
        </div>
      </div>
    </div>
  );
};

export default ChatModule;
