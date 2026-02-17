import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Dialog, useNotification } from '../ui/index.ts';
import { HttpClient, getToken } from '../../network/index.ts';
import './PlanetModule.css';

type TabScope = 'square' | 'mine' | 'favorite' | 'owner';
type VisibilityType = 'public' | 'hidden';
type ReactionType = 'like' | 'dislike' | 'none';

type PlanetBoundStrategyLite = {
  usId: number;
  aliasName: string;
  defName: string;
  state: string;
  versionNo: number;
};

type PlanetPostCard = {
  postId: number;
  uid: number;
  authorName: string;
  authorAvatarUrl?: string | null;
  title: string;
  content: string;
  visibility: VisibilityType;
  likeCount: number;
  dislikeCount: number;
  favoriteCount: number;
  commentCount: number;
  userReaction?: ReactionType | null;
  isFavorited: boolean;
  createdAt: string;
  updatedAt: string;
  imageUrls: string[];
  strategies: PlanetBoundStrategyLite[];
};

type PlanetPostListResponse = {
  page: number;
  pageSize: number;
  total: number;
  items: PlanetPostCard[];
};

type PlanetPositionLite = {
  positionId: number;
  exchange: string;
  symbol: string;
  side: string;
  entryPrice: number;
  qty: number;
  status: string;
  openedAt: string;
  closedAt?: string | null;
  realizedPnl?: number | null;
};

type PlanetStrategyDetail = {
  usId: number;
  defId?: number | null;
  aliasName: string;
  defName: string;
  description: string;
  state: string;
  versionNo: number;
  configJson?: unknown;
  positionHistory: PlanetPositionLite[];
};

type PlanetComment = {
  commentId: number;
  uid: number;
  authorName: string;
  authorAvatarUrl?: string | null;
  content: string;
  createdAt: string;
  updatedAt: string;
};

type PlanetPostDetail = {
  post: PlanetPostCard;
  comments: PlanetComment[];
  strategyDetails: PlanetStrategyDetail[];
  canManage: boolean;
};

type PlanetLiker = {
  uid: number;
  displayName: string;
  avatarUrl?: string | null;
  reactedAt: string;
};

type PlanetOwnerPostStats = {
  postId: number;
  title: string;
  visibility: VisibilityType;
  likeCount: number;
  dislikeCount: number;
  favoriteCount: number;
  commentCount: number;
  updatedAt: string;
  likers: PlanetLiker[];
};

type PlanetOwnerStatsResponse = {
  page: number;
  pageSize: number;
  total: number;
  items: PlanetOwnerPostStats[];
};

type StrategyOption = {
  usId: number;
  defName: string;
  aliasName: string;
  state: string;
  versionNo: number;
};

type StrategyListRecord = {
  usId: number;
  defName: string;
  aliasName: string;
  state: string;
  versionNo: number;
};

type PlanetImageUploadResponse = {
  imageUrl: string;
  objectKey: string;
};

const TAB_LABELS: Array<{ scope: TabScope; title: string; subtitle: string }> = [
  { scope: 'square', title: '星球广场', subtitle: '查看公开策略分享与交易讨论' },
  { scope: 'mine', title: '我的帖子', subtitle: '管理自己发布的内容与可见性' },
  { scope: 'favorite', title: '我的收藏', subtitle: '快速回看已收藏帖子' },
  { scope: 'owner', title: '互动统计', subtitle: '查看谁给我点赞与互动汇总' },
];

const formatDateTime = (value?: string | null) => {
  if (!value) {
    return '-';
  }
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }
  return date.toLocaleString('zh-CN', { hour12: false });
};

const PlanetModule: React.FC = () => {
  const client = useMemo(() => new HttpClient({ tokenProvider: getToken }), []);
  const { error: showError, success: showSuccess } = useNotification();
  const fileInputRef = useRef<HTMLInputElement>(null);

  const [activeTab, setActiveTab] = useState<TabScope>('square');
  const [posts, setPosts] = useState<PlanetPostCard[]>([]);
  const [ownerStats, setOwnerStats] = useState<PlanetOwnerPostStats[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [total, setTotal] = useState(0);
  const [actioningPostId, setActioningPostId] = useState<number | null>(null);

  const [isComposerOpen, setIsComposerOpen] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [editingPostId, setEditingPostId] = useState<number | null>(null);
  const [formTitle, setFormTitle] = useState('');
  const [formContent, setFormContent] = useState('');
  const [formVisibility, setFormVisibility] = useState<VisibilityType>('public');
  const [formImageUrls, setFormImageUrls] = useState<string[]>([]);
  const [formStrategyUsIds, setFormStrategyUsIds] = useState<number[]>([]);
  const [isUploadingImage, setIsUploadingImage] = useState(false);

  const [strategyOptions, setStrategyOptions] = useState<StrategyOption[]>([]);
  const [isStrategiesLoading, setIsStrategiesLoading] = useState(false);

  const [detailOpen, setDetailOpen] = useState(false);
  const [detailLoading, setDetailLoading] = useState(false);
  const [detail, setDetail] = useState<PlanetPostDetail | null>(null);
  const [commentDraft, setCommentDraft] = useState('');
  const [isSendingComment, setIsSendingComment] = useState(false);

  const resetComposer = useCallback(() => {
    setEditingPostId(null);
    setFormTitle('');
    setFormContent('');
    setFormVisibility('public');
    setFormImageUrls([]);
    setFormStrategyUsIds([]);
  }, []);

  const loadStrategies = useCallback(async () => {
    setIsStrategiesLoading(true);
    try {
      const data = await client.postProtocol<StrategyListRecord[]>('/api/strategy/list', 'strategy.list');
      const next = (Array.isArray(data) ? data : []).map((item) => ({
        usId: item.usId,
        defName: item.defName || '-',
        aliasName: item.aliasName || item.defName || `策略#${item.usId}`,
        state: item.state || 'unknown',
        versionNo: item.versionNo || 0,
      }));
      setStrategyOptions(next);
    } catch (err) {
      const message = err instanceof Error ? err.message : '加载策略列表失败';
      showError(message);
    } finally {
      setIsStrategiesLoading(false);
    }
  }, [client, showError]);

  const loadPosts = useCallback(
    async (scope: Exclude<TabScope, 'owner'>) => {
      setIsLoading(true);
      try {
        const data = await client.postProtocol<PlanetPostListResponse, { scope: string; page: number; pageSize: number }>(
          '/api/planet/posts/list',
          'planet.post.list',
          { scope, page: 1, pageSize: 20 },
        );
        setPosts(Array.isArray(data?.items) ? data.items : []);
        setTotal(data?.total ?? 0);
      } catch (err) {
        const message = err instanceof Error ? err.message : '加载帖子失败';
        showError(message);
      } finally {
        setIsLoading(false);
      }
    },
    [client, showError],
  );

  const loadOwnerStats = useCallback(async () => {
    setIsLoading(true);
    try {
      const data = await client.postProtocol<PlanetOwnerStatsResponse, { page: number; pageSize: number }>(
        '/api/planet/posts/owner/stats',
        'planet.post.owner.stats',
        { page: 1, pageSize: 20 },
      );
      setOwnerStats(Array.isArray(data?.items) ? data.items : []);
      setTotal(data?.total ?? 0);
    } catch (err) {
      const message = err instanceof Error ? err.message : '加载互动统计失败';
      showError(message);
    } finally {
      setIsLoading(false);
    }
  }, [client, showError]);

  const loadPostDetail = useCallback(
    async (postId: number, silent = false) => {
      if (!silent) {
        setDetailLoading(true);
      }
      try {
        const data = await client.postProtocol<PlanetPostDetail, { postId: number }>(
          '/api/planet/posts/detail',
          'planet.post.detail',
          { postId },
        );
        setDetail(data);
      } catch (err) {
        if (!silent) {
          const message = err instanceof Error ? err.message : '加载帖子详情失败';
          showError(message);
        }
      } finally {
        if (!silent) {
          setDetailLoading(false);
        }
      }
    },
    [client, showError],
  );

  const refreshCurrentTab = useCallback(async () => {
    if (activeTab === 'owner') {
      await loadOwnerStats();
      return;
    }
    await loadPosts(activeTab);
  }, [activeTab, loadOwnerStats, loadPosts]);

  useEffect(() => {
    refreshCurrentTab();
  }, [refreshCurrentTab]);

  const openComposerForCreate = async () => {
    resetComposer();
    setIsComposerOpen(true);
    await loadStrategies();
  };

  const openComposerForEdit = async (post: PlanetPostCard) => {
    setEditingPostId(post.postId);
    setFormTitle(post.title);
    setFormContent(post.content);
    setFormVisibility(post.visibility);
    setFormImageUrls([...post.imageUrls]);
    setFormStrategyUsIds(post.strategies.map((item) => item.usId));
    setIsComposerOpen(true);
    await loadStrategies();
  };

  const toggleStrategySelection = (usId: number) => {
    setFormStrategyUsIds((prev) => (prev.includes(usId) ? prev.filter((item) => item !== usId) : [...prev, usId]));
  };

  const handleImageUploadClick = () => {
    fileInputRef.current?.click();
  };

  const handleImageUpload = async (file: File) => {
    setIsUploadingImage(true);
    try {
      const formData = new FormData();
      formData.set('file', file);
      const data = await client.postForm<PlanetImageUploadResponse>('/api/planet/image/upload', 'planet.image.upload', formData);
      setFormImageUrls((prev) => (prev.includes(data.imageUrl) ? prev : [...prev, data.imageUrl]));
      showSuccess('图片上传成功');
    } catch (err) {
      const message = err instanceof Error ? err.message : '图片上传失败';
      showError(message);
    } finally {
      setIsUploadingImage(false);
    }
  };

  const handleFileChange = async (event: React.ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    event.target.value = '';
    if (!file) {
      return;
    }
    await handleImageUpload(file);
  };

  const removeImage = (url: string) => {
    setFormImageUrls((prev) => prev.filter((item) => item !== url));
  };

  const submitPost = async () => {
    if (isSubmitting) {
      return;
    }
    setIsSubmitting(true);
    try {
      const payload = {
        title: formTitle,
        content: formContent,
        visibility: formVisibility,
        imageUrls: formImageUrls,
        strategyUsIds: formStrategyUsIds,
      };
      if (editingPostId) {
        await client.postProtocol('/api/planet/posts/update', 'planet.post.update', { ...payload, postId: editingPostId });
      } else {
        await client.postProtocol('/api/planet/posts/create', 'planet.post.create', payload);
      }
      showSuccess(editingPostId ? '帖子更新成功' : '帖子发布成功');
      setIsComposerOpen(false);
      resetComposer();
      await refreshCurrentTab();
    } catch (err) {
      const message = err instanceof Error ? err.message : '提交帖子失败';
      showError(message);
    } finally {
      setIsSubmitting(false);
    }
  };

  const runPostAction = async (postId: number, action: () => Promise<void>) => {
    if (actioningPostId !== null) {
      return;
    }
    setActioningPostId(postId);
    try {
      await action();
      await refreshCurrentTab();
      if (detail?.post.postId === postId) {
        await loadPostDetail(postId, true);
      }
    } finally {
      setActioningPostId(null);
    }
  };

  const handleReaction = async (post: PlanetPostCard, target: 'like' | 'dislike') => {
    const nextReaction: ReactionType = post.userReaction === target ? 'none' : target;
    await runPostAction(post.postId, async () => {
      await client.postProtocol('/api/planet/posts/react', 'planet.post.react', {
        postId: post.postId,
        reactionType: nextReaction,
      });
    });
  };

  const handleFavorite = async (post: PlanetPostCard) => {
    await runPostAction(post.postId, async () => {
      await client.postProtocol('/api/planet/posts/favorite', 'planet.post.favorite', {
        postId: post.postId,
        isFavorite: !post.isFavorited,
      });
    });
  };

  const handleVisibilityToggle = async (post: PlanetPostCard) => {
    const nextVisibility: VisibilityType = post.visibility === 'public' ? 'hidden' : 'public';
    await runPostAction(post.postId, async () => {
      await client.postProtocol('/api/planet/posts/visibility', 'planet.post.visibility', {
        postId: post.postId,
        visibility: nextVisibility,
      });
    });
  };

  const handleDeletePost = async (post: PlanetPostCard) => {
    if (!window.confirm('确认删除该帖子吗？删除后不可恢复。')) {
      return;
    }
    await runPostAction(post.postId, async () => {
      await client.postProtocol('/api/planet/posts/delete', 'planet.post.delete', { postId: post.postId });
    });
  };

  const openDetail = async (postId: number) => {
    setDetailOpen(true);
    await loadPostDetail(postId);
  };

  const closeDetail = () => {
    setDetailOpen(false);
    setDetail(null);
    setCommentDraft('');
  };

  const submitComment = async () => {
    if (!detail || isSendingComment) {
      return;
    }
    const content = commentDraft.trim();
    if (!content) {
      showError('评论内容不能为空');
      return;
    }

    setIsSendingComment(true);
    try {
      await client.postProtocol('/api/planet/posts/comment/create', 'planet.post.comment.create', {
        postId: detail.post.postId,
        content,
      });
      setCommentDraft('');
      await loadPostDetail(detail.post.postId);
      await refreshCurrentTab();
    } catch (err) {
      const message = err instanceof Error ? err.message : '评论失败';
      showError(message);
    } finally {
      setIsSendingComment(false);
    }
  };

  const currentTabInfo = TAB_LABELS.find((item) => item.scope === activeTab) ?? TAB_LABELS[0];

  return (
    <div className="planet-module">
      <div className="planet-header">
        <div className="planet-header-main">
          <h2 className="planet-title">{currentTabInfo.title}</h2>
          <p className="planet-subtitle">{currentTabInfo.subtitle}</p>
        </div>
        <button type="button" className="planet-primary-btn" onClick={openComposerForCreate}>
          发布帖子
        </button>
      </div>

      <div className="planet-tabs">
        {TAB_LABELS.map((tab) => (
          <button
            key={tab.scope}
            type="button"
            className={`planet-tab ${activeTab === tab.scope ? 'is-active' : ''}`}
            onClick={() => setActiveTab(tab.scope)}
          >
            {tab.title}
          </button>
        ))}
        <span className="planet-total">总计 {total}</span>
      </div>

      {isLoading ? (
        <div className="planet-empty">加载中...</div>
      ) : activeTab === 'owner' ? (
        ownerStats.length === 0 ? (
          <div className="planet-empty">暂无互动数据</div>
        ) : (
          <div className="planet-owner-list">
            {ownerStats.map((item) => (
              <div key={item.postId} className="planet-owner-card">
                <div className="planet-owner-top">
                  <div className="planet-owner-title">{item.title}</div>
                  <span className={`planet-visibility ${item.visibility === 'public' ? 'is-public' : 'is-hidden'}`}>
                    {item.visibility === 'public' ? '公开' : '隐藏'}
                  </span>
                </div>
                <div className="planet-owner-counts">
                  <span>👍 {item.likeCount}</span>
                  <span>👎 {item.dislikeCount}</span>
                  <span>⭐ {item.favoriteCount}</span>
                  <span>💬 {item.commentCount}</span>
                  <span>更新于 {formatDateTime(item.updatedAt)}</span>
                </div>
                <div className="planet-liker-list">
                  {item.likers.length === 0 ? (
                    <span className="planet-liker-empty">暂无点赞用户</span>
                  ) : (
                    item.likers.map((liker) => (
                      <span key={`${item.postId}-${liker.uid}-${liker.reactedAt}`} className="planet-liker-item">
                        {liker.displayName}
                      </span>
                    ))
                  )}
                </div>
              </div>
            ))}
          </div>
        )
      ) : posts.length === 0 ? (
        <div className="planet-empty">暂无帖子</div>
      ) : (
        <div className="planet-post-list">
          {posts.map((post) => (
            <article key={post.postId} className="planet-post-card">
              <div className="planet-post-top">
                <div>
                  <div className="planet-post-title">{post.title}</div>
                  <div className="planet-post-meta">
                    <span>{post.authorName}</span>
                    <span>{formatDateTime(post.createdAt)}</span>
                    <span className={`planet-visibility ${post.visibility === 'public' ? 'is-public' : 'is-hidden'}`}>
                      {post.visibility === 'public' ? '公开' : '隐藏'}
                    </span>
                  </div>
                </div>
                <button type="button" className="planet-link-btn" onClick={() => openDetail(post.postId)}>
                  查看详情
                </button>
              </div>

              <div className="planet-post-content">{post.content}</div>

              {post.imageUrls.length > 0 && (
                <div className="planet-image-grid">
                  {post.imageUrls.map((url) => (
                    <img key={url} className="planet-post-image" src={url} alt="帖子图片" />
                  ))}
                </div>
              )}

              {post.strategies.length > 0 && (
                <div className="planet-strategy-tags">
                  {post.strategies.map((strategy) => (
                    <span key={`${post.postId}-${strategy.usId}`} className="planet-strategy-tag">
                      {strategy.aliasName} · V{strategy.versionNo}
                    </span>
                  ))}
                </div>
              )}

              <div className="planet-actions">
                <button
                  type="button"
                  disabled={actioningPostId === post.postId}
                  className={post.userReaction === 'like' ? 'is-active' : ''}
                  onClick={() => handleReaction(post, 'like')}
                >
                  👍 {post.likeCount}
                </button>
                <button
                  type="button"
                  disabled={actioningPostId === post.postId}
                  className={post.userReaction === 'dislike' ? 'is-active' : ''}
                  onClick={() => handleReaction(post, 'dislike')}
                >
                  👎 {post.dislikeCount}
                </button>
                <button
                  type="button"
                  disabled={actioningPostId === post.postId}
                  className={post.isFavorited ? 'is-active' : ''}
                  onClick={() => handleFavorite(post)}
                >
                  ⭐ {post.favoriteCount}
                </button>
                <span className="planet-comments-count">💬 {post.commentCount}</span>
                {activeTab === 'mine' && (
                  <>
                    <button type="button" disabled={actioningPostId === post.postId} onClick={() => handleVisibilityToggle(post)}>
                      {post.visibility === 'public' ? '设为隐藏' : '设为公开'}
                    </button>
                    <button type="button" disabled={actioningPostId === post.postId} onClick={() => openComposerForEdit(post)}>
                      编辑
                    </button>
                    <button type="button" disabled={actioningPostId === post.postId} onClick={() => handleDeletePost(post)}>
                      删除
                    </button>
                  </>
                )}
              </div>
            </article>
          ))}
        </div>
      )}

      <Dialog
        open={isComposerOpen}
        onClose={() => {
          setIsComposerOpen(false);
          resetComposer();
        }}
        title={editingPostId ? '编辑帖子' : '发布帖子'}
        confirmText={isSubmitting ? '提交中...' : editingPostId ? '保存修改' : '立即发布'}
        cancelText="取消"
        onConfirm={submitPost}
      >
        <div className="planet-composer">
          <label className="planet-field">
            <span>标题</span>
            <input value={formTitle} onChange={(event) => setFormTitle(event.target.value)} maxLength={128} />
          </label>
          <label className="planet-field">
            <span>正文</span>
            <textarea value={formContent} onChange={(event) => setFormContent(event.target.value)} rows={6} maxLength={5000} />
          </label>
          <label className="planet-field">
            <span>可见性</span>
            <select value={formVisibility} onChange={(event) => setFormVisibility(event.target.value as VisibilityType)}>
              <option value="public">公开</option>
              <option value="hidden">隐藏</option>
            </select>
          </label>

          <div className="planet-field">
            <span>图片</span>
            <div className="planet-upload-row">
              <button type="button" onClick={handleImageUploadClick} disabled={isUploadingImage || formImageUrls.length >= 9}>
                {isUploadingImage ? '上传中...' : '上传图片'}
              </button>
              <span>最多 9 张</span>
            </div>
            <div className="planet-image-list">
              {formImageUrls.map((url) => (
                <div key={url} className="planet-image-item">
                  <img src={url} alt="已上传图片" />
                  <button type="button" onClick={() => removeImage(url)}>
                    移除
                  </button>
                </div>
              ))}
            </div>
            <input ref={fileInputRef} type="file" accept="image/*" hidden onChange={handleFileChange} />
          </div>

          <div className="planet-field">
            <span>绑定策略</span>
            {isStrategiesLoading ? (
              <div className="planet-hint">策略加载中...</div>
            ) : strategyOptions.length === 0 ? (
              <div className="planet-hint">暂无可绑定策略</div>
            ) : (
              <div className="planet-strategy-options">
                {strategyOptions.map((strategy) => (
                  <label key={strategy.usId} className="planet-strategy-option">
                    <input
                      type="checkbox"
                      checked={formStrategyUsIds.includes(strategy.usId)}
                      onChange={() => toggleStrategySelection(strategy.usId)}
                    />
                    <span>{strategy.aliasName} · V{strategy.versionNo} · {strategy.state}</span>
                  </label>
                ))}
              </div>
            )}
          </div>
        </div>
      </Dialog>

      <Dialog open={detailOpen} onClose={closeDetail} title="帖子详情" cancelText="关闭">
        {detailLoading || !detail ? (
          <div className="planet-empty">加载中...</div>
        ) : (
          <div className="planet-detail">
            <div className="planet-detail-title">{detail.post.title}</div>
            <div className="planet-post-meta">
              <span>{detail.post.authorName}</span>
              <span>{formatDateTime(detail.post.createdAt)}</span>
            </div>
            <div className="planet-post-content">{detail.post.content}</div>

            {detail.post.imageUrls.length > 0 && (
              <div className="planet-image-grid">
                {detail.post.imageUrls.map((url) => (
                  <img key={url} className="planet-post-image" src={url} alt="帖子图片" />
                ))}
              </div>
            )}

            <div className="planet-actions">
              <button
                type="button"
                className={detail.post.userReaction === 'like' ? 'is-active' : ''}
                onClick={() => handleReaction(detail.post, 'like')}
              >
                👍 {detail.post.likeCount}
              </button>
              <button
                type="button"
                className={detail.post.userReaction === 'dislike' ? 'is-active' : ''}
                onClick={() => handleReaction(detail.post, 'dislike')}
              >
                👎 {detail.post.dislikeCount}
              </button>
              <button type="button" className={detail.post.isFavorited ? 'is-active' : ''} onClick={() => handleFavorite(detail.post)}>
                ⭐ {detail.post.favoriteCount}
              </button>
            </div>

            <div className="planet-section-title">绑定策略详情</div>
            {detail.strategyDetails.length === 0 ? (
              <div className="planet-hint">未绑定策略</div>
            ) : (
              detail.strategyDetails.map((strategy) => (
                <div key={strategy.usId} className="planet-strategy-detail-card">
                  <div className="planet-strategy-detail-title">
                    {strategy.aliasName}（{strategy.defName}） · V{strategy.versionNo}
                  </div>
                  <div className="planet-hint">{strategy.description || '暂无策略说明'}</div>
                  <details>
                    <summary>查看策略配置</summary>
                    <pre>{strategy.configJson ? JSON.stringify(strategy.configJson, null, 2) : '暂无配置'}</pre>
                  </details>
                  <details>
                    <summary>查看历史开仓（{strategy.positionHistory.length}）</summary>
                    <div className="planet-position-list">
                      {strategy.positionHistory.map((position) => (
                        <div key={position.positionId} className="planet-position-item">
                          <span>{position.symbol}</span>
                          <span>{position.side}</span>
                          <span>开仓: {formatDateTime(position.openedAt)}</span>
                          <span>状态: {position.status}</span>
                        </div>
                      ))}
                    </div>
                  </details>
                </div>
              ))
            )}

            <div className="planet-section-title">评论区</div>
            <div className="planet-comment-editor">
              <textarea
                value={commentDraft}
                onChange={(event) => setCommentDraft(event.target.value)}
                rows={3}
                maxLength={1000}
                placeholder="说点什么..."
              />
              <button type="button" onClick={submitComment} disabled={isSendingComment}>
                {isSendingComment ? '发送中...' : '发表评论'}
              </button>
            </div>
            <div className="planet-comment-list">
              {detail.comments.length === 0 ? (
                <div className="planet-hint">暂无评论</div>
              ) : (
                detail.comments.map((comment) => (
                  <div key={comment.commentId} className="planet-comment-item">
                    <div className="planet-comment-author">{comment.authorName}</div>
                    <div>{comment.content}</div>
                    <div className="planet-comment-time">{formatDateTime(comment.createdAt)}</div>
                  </div>
                ))
              )}
            </div>
          </div>
        )}
      </Dialog>
    </div>
  );
};

export default PlanetModule;
