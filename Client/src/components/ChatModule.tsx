import React from 'react';

const ChatModule: React.FC = () => {
  const messages = [
    { from: '系统提示', time: '09:30', text: '这里是量化助手聊天占位区域，目前所有内容均为示例。' },
    { from: '你', time: '09:32', text: '帮我看看今天有哪些策略回撤超过 5%？' },
    { from: '量化助手', time: '09:32', text: '这里会展示一段示例回复：当前没有真实风控数据接入，仅做界面展示使用。' },
  ];

  return (
    <div className="module-container">
      <div className="page-title">
        <h1 className="title-text">聊天 & 助手</h1>
        <span className="title-subtext">当前为静态占位消息，不做真实对话，仅展示布局</span>
      </div>
      <div className="module-card chat-module-card">
        <div className="chat-messages">
          {messages.map((m, index) => (
            <div key={index} className="chat-message">
              <div className="chat-message-header">
                <span className="chat-message-from">{m.from}</span>
                <span className="chat-message-time">{m.time}</span>
              </div>
              <div className="chat-message-text">{m.text}</div>
            </div>
          ))}
        </div>
        <div className="chat-input-placeholder">
          <span>输入框占位：后续可以在这里接入真实聊天输入组件</span>
        </div>
      </div>
    </div>
  );
};

export default ChatModule;

