import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';

import { HttpClient, HttpError, getToken } from '../../network/index.ts';
import type { StrategyConfig } from './StrategyModule.types';
import type { AiStrategySource } from './strategyAiBridge';
import { normalizeStrategyConfig } from './strategyConfigNormalizer';

type MessageRole = 'user' | 'assistant' | 'system';

type ChatMessage = {
  id: string;
  messageId?: number;
  role: MessageRole;
  from: string;
  time: string;
  text: string;
  strategyConfig?: StrategyConfig;
  strategyJson?: string;
  loading?: boolean;
};

type ConversationItem = {
  conversationId: number;
  title: string;
  lastMessagePreview?: string | null;
  createdAt: string;
  updatedAt: string;
  lastMessageAt: string;
};

type ConversationMessageItem = {
  messageId: number;
  role: string;
  text: string;
  strategyConfigJson?: string | null;
  createdAt: string;
};

type AiAssistantConversationListResponse = {
  items?: ConversationItem[];
};

type AiAssistantConversationMessagesResponse = {
  conversation?: ConversationItem;
  messages?: ConversationMessageItem[];
};

type AiAssistantChatResponse = {
  conversationId?: number;
  conversationTitle?: string;
  reply?: string;
  strategyConfig?: Record<string, unknown> | null;
};

type StrategyAiFloatingChatProps = {
  openRequest?: {
    requestId: number;
  } | null;
  buildCurrentContextRequest: () => {
    rawMessage: string;
    displayText: string;
  };
  onImportRisk: (source: AiStrategySource) => void;
  onImportLogic: (source: AiStrategySource) => void;
};

const AI_CHAT_TIMEOUT_MS = 120000;
const DEFAULT_PANEL_WIDTH = 430;
const DEFAULT_PANEL_HEIGHT = 620;
const MIN_PANEL_WIDTH = 360;
const MIN_PANEL_HEIGHT = 420;
const WORKBENCH_CONTEXT_MARKER = '[WORKBENCH_CONTEXT]';

const StrategyAiFloatingChat: React.FC<StrategyAiFloatingChatProps> = ({
  openRequest,
  buildCurrentContextRequest,
  onImportRisk,
  onImportLogic,
}) => {
  const client = useMemo(() => new HttpClient({ tokenProvider: getToken }), []);
  const [open, setOpen] = useState(false);
  const [isInitializing, setIsInitializing] = useState(false);
  const [isLoadingMessages, setIsLoadingMessages] = useState(false);
  const [isSending, setIsSending] = useState(false);
  const [sidebarCollapsed, setSidebarCollapsed] = useState(false);
  const [showLauncher, setShowLauncher] = useState(true);
  const [inputValue, setInputValue] = useState('');
  const [conversations, setConversations] = useState<ConversationItem[]>([]);
  const [activeConversationId, setActiveConversationId] = useState<number | null>(null);
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [expandedStrategyMap, setExpandedStrategyMap] = useState<Record<string, boolean>>({});
  const [panelRect, setPanelRect] = useState(() => createInitialPanelRect());
  const loadRequestRef = useRef(0);
  const openRequestRef = useRef(0);
  const panelRef = useRef<HTMLDivElement | null>(null);
  const scrollAnchorRef = useRef<HTMLDivElement | null>(null);
  const dragStateRef = useRef<{
    pointerId: number;
    startX: number;
    startY: number;
    left: number;
    top: number;
  } | null>(null);
  const resizeStateRef = useRef<{
    pointerId: number;
    startX: number;
    startY: number;
    width: number;
    height: number;
  } | null>(null);

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

      const rows = Array.isArray(response?.messages) ? response.messages : [];
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

  const createConversation = useCallback(async () => {
    const created = await client.postProtocol<ConversationItem>(
      '/api/ai-assistant/conversations/create',
      'ai.assistant.conversation.create',
      {},
    );

    if (!created?.conversationId) {
      throw new Error('创建对话失败');
    }

    setConversations((prev) => [created, ...prev.filter((item) => item.conversationId !== created.conversationId)]);
    setActiveConversationId(created.conversationId);
    setMessages([]);
    return created.conversationId;
  }, [client]);

  const ensureConversationId = useCallback(async () => {
    if (activeConversationId && activeConversationId > 0) {
      return activeConversationId;
    }
    return await createConversation();
  }, [activeConversationId, createConversation]);

  const initializeConversations = useCallback(async () => {
    setIsInitializing(true);
    try {
      const response = await client.postProtocol<AiAssistantConversationListResponse>(
        '/api/ai-assistant/conversations/list',
        'ai.assistant.conversation.list',
        { limit: 30 },
      );

      const list = Array.isArray(response?.items)
        ? response.items.filter((item) => item && item.conversationId > 0)
        : [];

      setConversations(list);
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
    } finally {
      setIsInitializing(false);
    }
  }, [client]);

  const sendMessage = useCallback(async (
    rawMessage: string,
    options?: {
      displayText?: string;
      loadingText?: string;
      conversationId?: number | null;
    },
  ) => {
    const trimmed = rawMessage.trim();
    if (!trimmed || isSending) {
      return;
    }

    const conversationId = options?.conversationId && options.conversationId > 0
      ? options.conversationId
      : await ensureConversationId();
    if (!conversationId) {
      return;
    }

    setShowLauncher(false);
    const userMessage: ChatMessage = {
      id: createMessageId(),
      role: 'user',
      from: '你',
      time: formatNowTime(),
      text: options?.displayText?.trim() || trimmed,
    };

    const loadingMessage: ChatMessage = {
      id: createMessageId(),
      role: 'assistant',
      from: '多维 AI',
      time: formatNowTime(),
      text: options?.loadingText || '正在分析当前策略，请稍候...',
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
          conversationId,
          message: trimmed,
        },
        {
          timeoutMs: AI_CHAT_TIMEOUT_MS,
        },
      );

      const strategyConfig = parseStrategyConfig(response?.strategyConfig);
      const replyText = response?.reply?.trim() || '已收到。';
      const assistantMessage: ChatMessage = {
        id: createMessageId(),
        role: 'assistant',
        from: '多维 AI',
        time: formatNowTime(),
        text: replyText,
        strategyConfig,
        strategyJson: strategyConfig ? JSON.stringify(strategyConfig, null, 2) : undefined,
      };

      setMessages((prev) => prev.map((item) => (item.id === loadingMessage.id ? assistantMessage : item)));

      const nowIso = new Date().toISOString();
      setConversations((prev) =>
        touchConversation(prev, {
          conversationId,
          conversationTitle: response?.conversationTitle,
          lastMessagePreview: replyText,
          nowIso,
        }),
      );
      setActiveConversationId(conversationId);
      setShowLauncher(false);
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
  }, [client, ensureConversationId, isSending]);

  useEffect(() => {
    if (!open) {
      return;
    }
    void initializeConversations();
  }, [initializeConversations, open]);

  useEffect(() => {
    if (!openRequest || openRequest.requestId === openRequestRef.current) {
      return;
    }
    openRequestRef.current = openRequest.requestId;
    setOpen(true);
    setShowLauncher(true);
    setSidebarCollapsed(false);
    setActiveConversationId(null);
    setMessages([]);
    setExpandedStrategyMap({});
    setInputValue('');
  }, [openRequest]);

  useEffect(() => {
    scrollAnchorRef.current?.scrollIntoView({ behavior: 'smooth', block: 'end' });
  }, [messages, isLoadingMessages, isSending]);

  useEffect(() => {
    const handleResize = () => {
      setPanelRect((prev) => {
        const rect = panelRef.current?.getBoundingClientRect();
        return clampPanelRect({
          left: rect ? rect.left : prev.left,
          top: rect ? rect.top : prev.top,
          width: rect ? rect.width : prev.width,
          height: rect ? rect.height : prev.height,
        });
      });
    };
    window.addEventListener('resize', handleResize);
    return () => {
      window.removeEventListener('resize', handleResize);
    };
  }, []);

  const handleStartDrag = (event: React.PointerEvent<HTMLDivElement>) => {
    const target = event.target as HTMLElement | null;
    if (target?.closest('button, textarea, input, select')) {
      return;
    }
    const rect = panelRef.current?.getBoundingClientRect();
    dragStateRef.current = {
      pointerId: event.pointerId,
      startX: event.clientX,
      startY: event.clientY,
      left: rect ? rect.left : panelRect.left,
      top: rect ? rect.top : panelRect.top,
    };
    event.currentTarget.setPointerCapture(event.pointerId);
  };

  const handleDragMove = (event: React.PointerEvent<HTMLDivElement>) => {
    if (!dragStateRef.current || dragStateRef.current.pointerId !== event.pointerId) {
      return;
    }
    const deltaX = event.clientX - dragStateRef.current.startX;
    const deltaY = event.clientY - dragStateRef.current.startY;
    setPanelRect((prev) =>
      clampPanelRect({
        ...prev,
        left: dragStateRef.current ? dragStateRef.current.left + deltaX : prev.left,
        top: dragStateRef.current ? dragStateRef.current.top + deltaY : prev.top,
      }),
    );
  };

  const handleEndDrag = (event: React.PointerEvent<HTMLDivElement>) => {
    if (!dragStateRef.current || dragStateRef.current.pointerId !== event.pointerId) {
      return;
    }
    dragStateRef.current = null;
    if (event.currentTarget.hasPointerCapture(event.pointerId)) {
      event.currentTarget.releasePointerCapture(event.pointerId);
    }
  };

  const handleStartResize = (event: React.PointerEvent<HTMLDivElement>) => {
    event.stopPropagation();
    const rect = panelRef.current?.getBoundingClientRect();
    resizeStateRef.current = {
      pointerId: event.pointerId,
      startX: event.clientX,
      startY: event.clientY,
      width: rect ? rect.width : panelRect.width,
      height: rect ? rect.height : panelRect.height,
    };
    event.currentTarget.setPointerCapture(event.pointerId);
  };

  const handleResizeMove = (event: React.PointerEvent<HTMLDivElement>) => {
    if (!resizeStateRef.current || resizeStateRef.current.pointerId !== event.pointerId) {
      return;
    }
    const deltaX = event.clientX - resizeStateRef.current.startX;
    const deltaY = event.clientY - resizeStateRef.current.startY;
    setPanelRect((prev) =>
      clampPanelRect({
        ...prev,
        width: resizeStateRef.current ? resizeStateRef.current.width + deltaX : prev.width,
        height: resizeStateRef.current ? resizeStateRef.current.height + deltaY : prev.height,
      }),
    );
  };

  const handleEndResize = (event: React.PointerEvent<HTMLDivElement>) => {
    if (!resizeStateRef.current || resizeStateRef.current.pointerId !== event.pointerId) {
      return;
    }
    resizeStateRef.current = null;
    if (event.currentTarget.hasPointerCapture(event.pointerId)) {
      event.currentTarget.releasePointerCapture(event.pointerId);
    }
  };

  const handleCreateConversation = async () => {
    try {
      await createConversation();
      setShowLauncher(false);
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

  const handleSelectConversation = async (conversationId: number) => {
    setActiveConversationId(conversationId);
    setMessages([]);
    setShowLauncher(false);
    await loadConversationMessages(conversationId);
  };

  const handleUseCurrentContext = async () => {
    try {
      const payload = buildCurrentContextRequest();
      let conversationId = activeConversationId;
      if (!conversationId) {
        conversationId = await createConversation();
      }
      setShowLauncher(false);
      await sendMessage(payload.rawMessage, {
        displayText: payload.displayText,
        loadingText: '正在快速理解当前策略状态...',
        conversationId,
      });
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

  const inputDisabled = isInitializing || isLoadingMessages || isSending;

  if (!open) {
    return null;
  }

  return (
    <div
      ref={panelRef}
      className="strategy-ai-floating-chat"
      style={{
        left: `${panelRect.left}px`,
        top: `${panelRect.top}px`,
        width: `${panelRect.width}px`,
        height: `${panelRect.height}px`,
      }}
    >
      <div
        className="strategy-ai-floating-chat-header"
        onPointerDown={handleStartDrag}
        onPointerMove={handleDragMove}
        onPointerUp={handleEndDrag}
        onPointerCancel={handleEndDrag}
      >
        <div className="strategy-ai-floating-chat-title-wrap">
          <div className="strategy-ai-floating-chat-title">多维 AI 协同助手</div>
          <div className="strategy-ai-floating-chat-subtitle">边看工作台边和 AI 交互，支持直接导入策略结果</div>
        </div>
        <div className="strategy-ai-floating-chat-header-actions">
          <button
            type="button"
            className="strategy-ai-floating-chat-header-btn is-primary"
            onClick={() => {
              setShowLauncher(true);
              setSidebarCollapsed(false);
              setMessages([]);
              setActiveConversationId(null);
            }}
          >
            入口页
          </button>
          <button
            type="button"
            className="strategy-ai-floating-chat-header-btn"
            onClick={() => setSidebarCollapsed((prev) => !prev)}
          >
            {sidebarCollapsed ? '会话' : '收起会话'}
          </button>
          <button
            type="button"
            className="strategy-ai-floating-chat-header-btn"
            onClick={() => {
              void handleCreateConversation();
            }}
          >
            新建聊天
          </button>
          <button
            type="button"
            className="strategy-ai-floating-chat-header-btn"
            onClick={() => setOpen(false)}
          >
            关闭
          </button>
        </div>
      </div>

      <div className="strategy-ai-floating-chat-body">
        {!sidebarCollapsed && (
          <aside className="strategy-ai-floating-chat-sidebar ui-scrollable">
            {conversations.map((item) => (
              <button
                key={item.conversationId}
                type="button"
                className={`strategy-ai-floating-chat-conversation${item.conversationId === activeConversationId ? ' is-active' : ''}`}
                onClick={() => {
                  void handleSelectConversation(item.conversationId);
                }}
              >
                <div className="strategy-ai-floating-chat-conversation-title">{item.title || '新对话'}</div>
                <div className="strategy-ai-floating-chat-conversation-preview">{item.lastMessagePreview || '暂无消息'}</div>
                <div className="strategy-ai-floating-chat-conversation-time">{formatConversationTime(item.lastMessageAt)}</div>
              </button>
            ))}
            {!conversations.length && <div className="strategy-ai-floating-chat-empty">暂无会话</div>}
          </aside>
        )}

        <div className="strategy-ai-floating-chat-main">
          <div className="strategy-ai-floating-chat-messages ui-scrollable">
            {isInitializing && <div className="strategy-ai-floating-chat-empty">正在加载 AI 会话...</div>}
            {!isInitializing && showLauncher && !isLoadingMessages && (
              <div className="strategy-ai-floating-chat-launcher">
                <div className="strategy-ai-floating-chat-launcher-title">多维AI</div>
                <div className="strategy-ai-floating-chat-launcher-hint">
                  你可以继续历史聊天，也可以新建对话，或者先让 AI 快速了解当前工作台策略状态。
                </div>
                <div className="strategy-ai-floating-chat-launcher-actions">
                  <button
                    type="button"
                    className="strategy-ai-floating-chat-inline-btn"
                    onClick={() => {
                      void handleCreateConversation();
                    }}
                  >
                    新建对话
                  </button>
                  <button
                    type="button"
                    className="strategy-ai-floating-chat-inline-btn is-primary"
                    onClick={() => {
                      void handleUseCurrentContext();
                    }}
                  >
                    依据当前情况和AI交互
                  </button>
                </div>
              </div>
            )}
            {!isInitializing && !showLauncher && messages.length === 0 && !isLoadingMessages && (
              <div className="strategy-ai-floating-chat-empty">当前会话暂无消息，可以继续提问或先让 AI 了解当前策略。</div>
            )}

            {messages.map((message) => {
              const currentConversation = conversations.find((item) => item.conversationId === activeConversationId);
              const source: AiStrategySource | null = message.strategyConfig && message.strategyJson
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
                  className={`strategy-ai-floating-chat-message is-${message.role}${message.loading ? ' is-loading' : ''}`}
                >
                  <div className="strategy-ai-floating-chat-message-header">
                    <span>{message.from}</span>
                    <span>{message.time}</span>
                  </div>
                  <div className="strategy-ai-floating-chat-message-text">{message.text}</div>

                  {message.strategyJson && (
                    <div className="strategy-ai-floating-chat-strategy">
                      <div className="strategy-ai-floating-chat-strategy-header">
                        <div>
                          <div className="strategy-ai-floating-chat-strategy-title">策略 JSON</div>
                          <div className="strategy-ai-floating-chat-strategy-hint">展开后支持滚动查看详情</div>
                        </div>
                        <button
                          type="button"
                          className="strategy-ai-floating-chat-inline-btn"
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
                        <div className="strategy-ai-floating-chat-strategy-actions">
                          <button
                            type="button"
                            className="strategy-ai-floating-chat-inline-btn is-primary"
                            onClick={() => onImportRisk(source)}
                          >
                            导入止盈止损/移动止盈止损
                          </button>
                          <button
                            type="button"
                            className="strategy-ai-floating-chat-inline-btn"
                            onClick={() => onImportLogic(source)}
                          >
                            导入条件判断
                          </button>
                        </div>
                      )}
                      {expandedStrategyMap[message.id] && (
                        <pre className="strategy-ai-floating-chat-strategy-json">{message.strategyJson}</pre>
                      )}
                    </div>
                  )}
                </div>
              );
            })}

            <div ref={scrollAnchorRef} />
          </div>

          <div className="strategy-ai-floating-chat-input-wrap">
            <textarea
              className="strategy-ai-floating-chat-input"
              placeholder="继续追问当前策略、让 AI 帮你调整条件或解释逻辑..."
              value={inputValue}
              onChange={(event) => setInputValue(event.target.value)}
              onKeyDown={(event) => {
                if (event.key === 'Enter' && !event.shiftKey) {
                  event.preventDefault();
                  void sendMessage(inputValue);
                }
              }}
              rows={2}
              disabled={inputDisabled}
            />
            <button
              type="button"
              className="strategy-ai-floating-chat-send-btn"
              disabled={!inputValue.trim() || inputDisabled}
              onClick={() => {
                void sendMessage(inputValue);
              }}
            >
              发送
            </button>
          </div>
        </div>
      </div>
      <div
        className="strategy-ai-floating-chat-resize-handle"
        onPointerDown={handleStartResize}
        onPointerMove={handleResizeMove}
        onPointerUp={handleEndResize}
        onPointerCancel={handleEndResize}
      />
    </div>
  );
};

function createInitialPanelRect() {
  const width = DEFAULT_PANEL_WIDTH;
  const height = DEFAULT_PANEL_HEIGHT;
  const viewportWidth = typeof window !== 'undefined' ? window.innerWidth : 1600;
  const viewportHeight = typeof window !== 'undefined' ? window.innerHeight : 900;
  return clampPanelRect({
    left: viewportWidth - width - 28,
    top: viewportHeight - height - 28,
    width,
    height,
  });
}

function clampPanelRect(rect: { left: number; top: number; width: number; height: number }) {
  const viewportWidth = typeof window !== 'undefined' ? window.innerWidth : 1600;
  const viewportHeight = typeof window !== 'undefined' ? window.innerHeight : 900;
  const width = Math.min(Math.max(rect.width, MIN_PANEL_WIDTH), Math.max(MIN_PANEL_WIDTH, viewportWidth - 24));
  const height = Math.min(Math.max(rect.height, MIN_PANEL_HEIGHT), Math.max(MIN_PANEL_HEIGHT, viewportHeight - 80));
  const left = Math.min(Math.max(rect.left, 12), Math.max(12, viewportWidth - width - 12));
  const top = Math.min(Math.max(rect.top, 68), Math.max(68, viewportHeight - height - 12));
  return {
    left,
    top,
    width,
    height,
  };
}

function mapHistoryMessage(item: ConversationMessageItem): ChatMessage {
  const role = normalizeRole(item.role);
  const strategyConfig = parseStrategyConfig(item.strategyConfigJson);
  return {
    id: `history-${item.messageId}-${item.createdAt}`,
    messageId: item.messageId,
    role,
    from: mapRoleLabel(role),
    time: formatConversationTime(item.createdAt),
    text: mapDisplayText(item.text || ''),
    strategyConfig,
    strategyJson: strategyConfig ? JSON.stringify(strategyConfig, null, 2) : undefined,
  };
}

function normalizeRole(role: string): MessageRole {
  const value = (role || '').trim().toLowerCase();
  if (value === 'user') {
    return 'user';
  }
  if (value === 'assistant') {
    return 'assistant';
  }
  return 'system';
}

function mapRoleLabel(role: MessageRole): string {
  if (role === 'user') {
    return '你';
  }
  if (role === 'assistant') {
    return '多维 AI';
  }
  return '系统';
}

function parseStrategyConfig(raw?: Record<string, unknown> | string | null): StrategyConfig | undefined {
  return normalizeStrategyConfig(raw);
}

function mapDisplayText(text: string): string {
  if ((text || '').includes(WORKBENCH_CONTEXT_MARKER)) {
    return '这是当前我编辑的策略状态，快速了解一下。';
  }
  return text;
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

function formatNowTime(): string {
  const now = new Date();
  const hour = now.getHours().toString().padStart(2, '0');
  const minute = now.getMinutes().toString().padStart(2, '0');
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
  const month = `${parsed.getMonth() + 1}`.padStart(2, '0');
  const day = `${parsed.getDate()}`.padStart(2, '0');
  const hour = `${parsed.getHours()}`.padStart(2, '0');
  const minute = `${parsed.getMinutes()}`.padStart(2, '0');
  return `${month}-${day} ${hour}:${minute}`;
}

function createMessageId() {
  return `${Date.now()}-${Math.random().toString(36).slice(2, 9)}`;
}

export default StrategyAiFloatingChat;
