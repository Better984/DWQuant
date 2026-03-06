import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import './ChatModule.css';
import { HttpClient, HttpError, getToken } from '../../network/index.ts';
import type { StrategyConfig } from '../strategy/StrategyModule.types';
import {
  openStrategySaveDialogFromAi,
  openStrategyWorkbenchFromAi,
} from '../strategy/strategyAiBridge';

type MessageRole = 'user' | 'assistant' | 'system';

interface ChatMessage {
  id: string;
  messageId?: number;
  role: MessageRole;
  from: string;
  time: string;
  text: string;
  strategyConfig?: StrategyConfig;
  strategyJson?: string;
  suggestedQuestions?: string[];
  loading?: boolean;
}

interface ConversationItem {
  conversationId: number;
  title: string;
  lastMessagePreview?: string | null;
  createdAt: string;
  updatedAt: string;
  lastMessageAt: string;
}

interface ConversationMessageItem {
  messageId: number;
  role: string;
  text: string;
  strategyConfigJson?: string | null;
  suggestedQuestionsJson?: string | null;
  createdAt: string;
}

interface AiAssistantConversationListResponse {
  items?: ConversationItem[];
}

interface AiAssistantConversationMessagesResponse {
  conversation?: ConversationItem;
  messages?: ConversationMessageItem[];
}

interface AiAssistantChatResponse {
  conversationId?: number;
  conversationTitle?: string;
  reply?: string;
  strategyConfig?: Record<string, unknown> | null;
  suggestedQuestions?: string[] | null;
}

const AI_CHAT_TIMEOUT_MS = 120000;
const DEFAULT_QUICK_QUESTIONS = [
  '帮我生成一个 RSI+MACD 策略',
  '给我一个 EMA15 上穿 SMA50 的策略',
  '当前支持哪些技术指标和条件方法',
];

const ChatModule: React.FC = () => {
  const client = useMemo(() => new HttpClient({ tokenProvider: getToken }), []);
  const [inputValue, setInputValue] = useState('');
  const [isSending, setIsSending] = useState(false);
  const [isInitializing, setIsInitializing] = useState(true);
  const [isLoadingMessages, setIsLoadingMessages] = useState(false);
  const [conversations, setConversations] = useState<ConversationItem[]>([]);
  const [activeConversationId, setActiveConversationId] = useState<number | null>(null);
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [expandedStrategyMap, setExpandedStrategyMap] = useState<Record<string, boolean>>({});
  const scrollAnchorRef = useRef<HTMLDivElement | null>(null);
  const loadRequestRef = useRef(0);

  const loadConversationMessages = useCallback(async (conversationId: number) => {
    if (!conversationId || conversationId <= 0) {
      return;
    }

    const requestId = ++loadRequestRef.current;
    setIsLoadingMessages(true);

    try {
      const response = await client.postProtocol<AiAssistantConversationMessagesResponse>(
        '/api/ai-assistant/conversations/messages',
        'ai.assistant.conversation.messages',
        {
          conversationId,
          limit: 200,
        },
      );

      if (requestId !== loadRequestRef.current) {
        return;
      }

      const rows = response?.messages ?? [];
      setMessages(rows.map(mapHistoryMessage));

      if (response?.conversation?.conversationId) {
        setConversations((prev) => upsertConversation(prev, response.conversation as ConversationItem));
      }
    } catch (error) {
      if (requestId !== loadRequestRef.current) {
        return;
      }

      setMessages([
        {
          id: createMessageId(),
          role: 'system',
          from: '系统',
          time: formatNowTime(),
          text: resolveErrorMessage(error),
        },
      ]);
    } finally {
      if (requestId === loadRequestRef.current) {
        setIsLoadingMessages(false);
      }
    }
  }, [client]);

  useEffect(() => {
    scrollAnchorRef.current?.scrollIntoView({ behavior: 'smooth', block: 'end' });
  }, [messages, isSending, isLoadingMessages]);

  useEffect(() => {
    let disposed = false;

    const initialize = async () => {
      setIsInitializing(true);
      try {
        const listResult = await client.postProtocol<AiAssistantConversationListResponse>(
          '/api/ai-assistant/conversations/list',
          'ai.assistant.conversation.list',
          { limit: 30 },
        );
        if (disposed) {
          return;
        }

        const list = (listResult?.items ?? []).filter((item) => item && item.conversationId > 0);
        if (list.length === 0) {
          const created = await client.postProtocol<ConversationItem>(
            '/api/ai-assistant/conversations/create',
            'ai.assistant.conversation.create',
            {},
          );
          if (disposed || !created?.conversationId) {
            return;
          }

          setConversations([created]);
          setActiveConversationId(created.conversationId);
          setMessages([]);
          return;
        }

        setConversations(list);
        setActiveConversationId(list[0].conversationId);
        await loadConversationMessages(list[0].conversationId);
      } catch (error) {
        if (disposed) {
          return;
        }

        setMessages([
          {
            id: createMessageId(),
            role: 'system',
            from: '系统',
            time: formatNowTime(),
            text: resolveErrorMessage(error),
          },
        ]);
      } finally {
        if (!disposed) {
          setIsInitializing(false);
        }
      }
    };

    void initialize();

    return () => {
      disposed = true;
    };
  }, [client, loadConversationMessages]);

  const handleSelectConversation = async (conversationId: number) => {
    if (conversationId <= 0 || conversationId === activeConversationId) {
      return;
    }

    setActiveConversationId(conversationId);
    setMessages([]);
    await loadConversationMessages(conversationId);
  };

  const handleCreateConversation = async () => {
    try {
      const created = await client.postProtocol<ConversationItem>(
        '/api/ai-assistant/conversations/create',
        'ai.assistant.conversation.create',
        {},
      );
      if (!created?.conversationId) {
        return;
      }

      setConversations((prev) => [created, ...prev.filter((item) => item.conversationId !== created.conversationId)]);
      setActiveConversationId(created.conversationId);
      setMessages([]);
      setInputValue('');
    } catch (error) {
      setMessages([
        {
          id: createMessageId(),
          role: 'system',
          from: '系统',
          time: formatNowTime(),
          text: resolveErrorMessage(error),
        },
      ]);
    }
  };

  const sendMessage = async (rawMessage: string) => {
    const trimmed = rawMessage.trim();
    if (!trimmed || isSending || !activeConversationId) {
      return;
    }

    const userMessage: ChatMessage = {
      id: createMessageId(),
      role: 'user',
      from: '你',
      time: formatNowTime(),
      text: trimmed,
    };

    const loadingMessage: ChatMessage = {
      id: createMessageId(),
      role: 'assistant',
      from: '量化助手',
      time: formatNowTime(),
      text: '正在生成策略配置，请稍候...',
      loading: true,
    };

    setMessages((prev) => [...prev, userMessage, loadingMessage]);
    setInputValue('');
    setIsSending(true);

    try {
      const response = await client.postProtocol<AiAssistantChatResponse>(
        '/api/ai-assistant/chat',
        'ai.assistant.chat',
        {
          conversationId: activeConversationId,
          message: trimmed,
        },
        {
          // AI 生成耗时明显高于普通接口，单独放宽等待时间，避免 15 秒默认超时过早中断。
          timeoutMs: AI_CHAT_TIMEOUT_MS,
        },
      );

      const replyText = response?.reply?.trim() || '已生成结果。';
      const strategyConfig = isPlainObject(response?.strategyConfig)
        ? (response.strategyConfig as StrategyConfig)
        : undefined;
      const strategyJson = strategyConfig ? JSON.stringify(strategyConfig, null, 2) : undefined;
      const suggestedQuestions = parseSuggestedQuestions(response?.suggestedQuestions);

      const assistantMessage: ChatMessage = {
        id: createMessageId(),
        role: 'assistant',
        from: '量化助手',
        time: formatNowTime(),
        text: replyText,
        strategyConfig,
        strategyJson,
        suggestedQuestions,
      };

      setMessages((prev) => prev.map((item) => (item.id === loadingMessage.id ? assistantMessage : item)));

      const nowIso = new Date().toISOString();
      setConversations((prev) =>
        touchConversation(prev, {
          conversationId: activeConversationId,
          conversationTitle: response?.conversationTitle,
          lastMessagePreview: replyText,
          nowIso,
        }),
      );
    } catch (error) {
      const errMessage = resolveErrorMessage(error);
      setMessages((prev) =>
        prev
          .filter((item) => item.id !== loadingMessage.id)
          .concat({
            id: createMessageId(),
            role: 'system',
            from: '系统',
            time: formatNowTime(),
            text: errMessage,
          }),
      );
    } finally {
      setIsSending(false);
    }
  };

  const handleSend = async () => {
    await sendMessage(inputValue);
  };

  const handleQuickQuestionClick = async (question: string) => {
    await sendMessage(question);
  };

  const inputDisabled = isInitializing || isLoadingMessages || isSending || !activeConversationId;

  return (
    <div className="module-container chat-module-container">
      <div className="page-title">
        <h1 className="title-text">AI 助手</h1>
      </div>
      <div className="chat-module-panel">
        <aside className="chat-conversation-sidebar">
          <div className="chat-conversation-toolbar">
            <button
              type="button"
              className="chat-new-conversation-btn"
              onClick={() => {
                void handleCreateConversation();
              }}
            >
              + 新建对话
            </button>
          </div>
          <div className="chat-conversation-list ui-scrollable">
            {conversations.map((item) => (
              <button
                key={item.conversationId}
                type="button"
                className={`chat-conversation-item${item.conversationId === activeConversationId ? ' is-active' : ''}`}
                onClick={() => {
                  void handleSelectConversation(item.conversationId);
                }}
              >
                <div className="chat-conversation-title">{item.title || '新对话'}</div>
                <div className="chat-conversation-preview">{item.lastMessagePreview || '暂无消息'}</div>
                <div className="chat-conversation-time">{formatConversationTime(item.lastMessageAt)}</div>
              </button>
            ))}
            {!conversations.length && <div className="chat-conversation-empty">暂无会话</div>}
          </div>
        </aside>

        <div className="chat-main-panel">
          <div className="chat-messages ui-scrollable">
            {isInitializing && <div className="chat-empty-tip">正在加载会话...</div>}
            {!isInitializing && isLoadingMessages && messages.length === 0 && <div className="chat-empty-tip">正在加载消息...</div>}
            {!isInitializing && !isLoadingMessages && messages.length === 0 && (
              <>
                <div className="chat-empty-tip">开始描述你的策略需求吧</div>
                <div className="chat-quick-questions">
                  <div className="chat-quick-questions-title">快捷提问</div>
                  <div className="chat-quick-questions-list">
                    {DEFAULT_QUICK_QUESTIONS.map((question) => (
                      <button
                        key={question}
                        type="button"
                        className="chat-quick-question-btn"
                        onClick={() => {
                          void handleQuickQuestionClick(question);
                        }}
                        disabled={inputDisabled}
                      >
                        {question}
                      </button>
                    ))}
                  </div>
                </div>
              </>
            )}

            {messages.map((message) => {
              const currentConversation = conversations.find((item) => item.conversationId === activeConversationId);
              const source = message.strategyConfig && message.strategyJson
                ? {
                    conversationId: currentConversation?.conversationId,
                    conversationTitle: currentConversation?.title,
                    messageId: message.messageId,
                    messageTime: message.time,
                    replyText: message.text,
                    strategyConfig: message.strategyConfig,
                    strategyJson: message.strategyJson,
                  }
                : null;

              return (
                <div
                  key={message.id}
                  className={`chat-message is-${message.role}${message.loading ? ' is-loading' : ''}`}
                >
                  <div className="chat-message-header">
                    <span className="chat-message-from">{message.from}</span>
                    <span className="chat-message-time">{message.time}</span>
                  </div>
                  <div className="chat-message-text">{message.text}</div>
                  {message.strategyJson && (
                    <div className="chat-strategy-block">
                      <div className="chat-strategy-block-header">
                        <div>
                          <div className="chat-strategy-block-title">可导入策略 JSON</div>
                          <div className="chat-strategy-block-hint">展开后支持滚动查看详情</div>
                        </div>
                        <button
                          type="button"
                          className="chat-strategy-toggle-btn"
                          onClick={() =>
                            setExpandedStrategyMap((prev) => ({
                              ...prev,
                              [message.id]: !prev[message.id],
                            }))
                          }
                        >
                          {expandedStrategyMap[message.id] ? '收起' : '展开'}
                        </button>
                      </div>
                      {source && (
                        <div className="chat-strategy-actions">
                          <button
                            type="button"
                            className="chat-strategy-action-btn is-primary"
                            onClick={() => openStrategyWorkbenchFromAi(source)}
                          >
                            前往调试界面
                          </button>
                          <button
                            type="button"
                            className="chat-strategy-action-btn"
                            onClick={() => openStrategySaveDialogFromAi(source)}
                          >
                            保存到我的策略
                          </button>
                        </div>
                      )}
                      {expandedStrategyMap[message.id] && (
                        <pre className="chat-strategy-json">{message.strategyJson}</pre>
                      )}
                    </div>
                  )}
                  {message.role === 'assistant' && message.suggestedQuestions && message.suggestedQuestions.length > 0 && (
                    <div className="chat-message-quick-actions">
                      <div className="chat-message-quick-actions-title">你可以继续问</div>
                      <div className="chat-message-quick-actions-list">
                        {message.suggestedQuestions.map((question) => (
                          <button
                            key={`${message.id}-${question}`}
                            type="button"
                            className="chat-message-quick-action-btn"
                            onClick={() => {
                              void handleQuickQuestionClick(question);
                            }}
                            disabled={inputDisabled}
                          >
                            {question}
                          </button>
                        ))}
                      </div>
                    </div>
                  )}
                </div>
              );
            })}
            <div ref={scrollAnchorRef} />
          </div>

          <div className="chat-input-wrap">
            <div className="chat-input-inner">
              <textarea
                className="chat-input"
                placeholder="输入问题：讨论行情，或描述你想要的策略（标的、周期、风控等）..."
                value={inputValue}
                onChange={(event) => setInputValue(event.target.value)}
                onKeyDown={(event) => {
                  if (event.key === 'Enter' && !event.shiftKey) {
                    event.preventDefault();
                    void handleSend();
                  }
                }}
                rows={1}
                aria-label="输入消息"
                disabled={inputDisabled}
              />
              <button
                type="button"
                className="chat-send-btn"
                onClick={() => {
                  void handleSend();
                }}
                disabled={!inputValue.trim() || inputDisabled}
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
    </div>
  );
};

function mapHistoryMessage(item: ConversationMessageItem): ChatMessage {
  const strategyConfig = parseStrategyConfig(item.strategyConfigJson);
  const role = normalizeRole(item.role);
  return {
    id: `history-${item.messageId}-${item.createdAt}`,
    messageId: item.messageId,
    role,
    from: mapFromLabel(role),
    time: formatMessageTime(item.createdAt),
    text: item.text || '',
    strategyConfig,
    strategyJson: strategyConfig ? JSON.stringify(strategyConfig, null, 2) : undefined,
    suggestedQuestions: parseSuggestedQuestions(item.suggestedQuestionsJson),
  };
}

function normalizeRole(role: string): MessageRole {
  const raw = (role || '').trim().toLowerCase();
  if (raw === 'user') {
    return 'user';
  }
  if (raw === 'assistant') {
    return 'assistant';
  }
  return 'system';
}

function mapFromLabel(role: MessageRole): string {
  if (role === 'user') {
    return '你';
  }
  if (role === 'assistant') {
    return '量化助手';
  }
  return '系统';
}

function parseStrategyConfig(raw?: string | null): StrategyConfig | undefined {
  if (!raw || !raw.trim()) {
    return undefined;
  }

  try {
    const parsed = JSON.parse(raw) as unknown;
    return isPlainObject(parsed) ? (parsed as StrategyConfig) : undefined;
  } catch {
    return undefined;
  }
}

function parseSuggestedQuestions(raw?: string[] | string | null): string[] | undefined {
  if (Array.isArray(raw)) {
    const items = raw
      .map((item) => (typeof item === 'string' ? item.trim() : ''))
      .filter((item, index, list) => item && list.indexOf(item) === index)
      .slice(0, 3);
    return items.length > 0 ? items : undefined;
  }

  if (!raw || !raw.trim()) {
    return undefined;
  }

  try {
    const parsed = JSON.parse(raw);
    return parseSuggestedQuestions(Array.isArray(parsed) ? parsed : []);
  } catch {
    return undefined;
  }
}

function upsertConversation(list: ConversationItem[], conversation: ConversationItem): ConversationItem[] {
  const next = list.filter((item) => item.conversationId !== conversation.conversationId);
  return [conversation, ...next];
}

function touchConversation(
  list: ConversationItem[],
  payload: {
    conversationId: number;
    conversationTitle?: string;
    lastMessagePreview: string;
    nowIso: string;
  },
): ConversationItem[] {
  const current = list.find((item) => item.conversationId === payload.conversationId);
  const nextItem: ConversationItem = {
    conversationId: payload.conversationId,
    title: payload.conversationTitle?.trim() || current?.title || '新对话',
    lastMessagePreview: payload.lastMessagePreview,
    createdAt: current?.createdAt || payload.nowIso,
    updatedAt: payload.nowIso,
    lastMessageAt: payload.nowIso,
  };

  return [nextItem, ...list.filter((item) => item.conversationId !== payload.conversationId)];
}

function resolveErrorMessage(error: unknown): string {
  if (error instanceof HttpError) {
    return error.message || '请求失败，请稍后重试。';
  }
  if (error instanceof Error) {
    return error.message || '请求失败，请稍后重试。';
  }
  return '请求失败，请稍后重试。';
}

function formatNowTime(): string {
  const now = new Date();
  const hour = now.getHours().toString().padStart(2, '0');
  const minute = now.getMinutes().toString().padStart(2, '0');
  return `${hour}:${minute}`;
}

function formatMessageTime(isoTime?: string): string {
  if (!isoTime) {
    return formatNowTime();
  }

  const parsed = new Date(isoTime);
  if (Number.isNaN(parsed.getTime())) {
    return formatNowTime();
  }

  const hour = parsed.getHours().toString().padStart(2, '0');
  const minute = parsed.getMinutes().toString().padStart(2, '0');
  return `${hour}:${minute}`;
}

function formatConversationTime(isoTime?: string): string {
  if (!isoTime) {
    return '--:--';
  }

  const parsed = new Date(isoTime);
  if (Number.isNaN(parsed.getTime())) {
    return '--:--';
  }

  const now = new Date();
  const sameDay = parsed.getFullYear() === now.getFullYear()
    && parsed.getMonth() === now.getMonth()
    && parsed.getDate() === now.getDate();

  if (sameDay) {
    const hour = parsed.getHours().toString().padStart(2, '0');
    const minute = parsed.getMinutes().toString().padStart(2, '0');
    return `${hour}:${minute}`;
  }

  const month = (parsed.getMonth() + 1).toString().padStart(2, '0');
  const day = parsed.getDate().toString().padStart(2, '0');
  return `${month}-${day}`;
}

function createMessageId(): string {
  return `${Date.now()}-${Math.random().toString(36).slice(2, 9)}`;
}

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === 'object' && !Array.isArray(value);
}

export default ChatModule;
