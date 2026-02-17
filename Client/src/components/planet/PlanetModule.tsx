import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import * as echarts from 'echarts';
import type { ECharts, EChartsOption } from 'echarts';
import { Dialog, useNotification } from '../ui/index.ts';
import { HttpClient, getToken } from '../../network/index.ts';
import './PlanetModule.css';

type TabScope = 'square' | 'mine' | 'favorite' | 'owner';
type PostStatusType = 'normal' | 'hidden' | 'deleted';
type ReactionType = 'like' | 'dislike' | 'none';

type PlanetBoundStrategyLite = {
  usId: number;
  aliasName: string;
  defName: string;
  state: string;
  versionNo: number;
  pnlSeries30d: number[];
  curveSource?: string;
  isBacktestCurve?: boolean;
};

type PlanetPostCard = {
  postId: number;
  uid: number;
  authorName: string;
  authorAvatarUrl?: string | null;
  title: string;
  content: string;
  status: PostStatusType;
  likeCount: number;
  dislikeCount: number;
  favoriteCount: number;
  commentCount: number;
  userReaction?: ReactionType | null;
  isFavorited: boolean;
  canManage: boolean;
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
  canDelete: boolean;
  createdAt: string;
  updatedAt: string;
};

type PlanetCommentListResponse = {
  postId: number;
  total: number;
  offset: number;
  limit: number;
  hasMore: boolean;
  items: PlanetComment[];
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
  status: PostStatusType;
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

type PlanetPostCreateResponse = {
  postId?: number;
};

type CommentPanelState = {
  open: boolean;
  loaded: boolean;
  loading: boolean;
  posting: boolean;
  deletingCommentId: number | null;
  draft: string;
  total: number;
  offset: number;
  hasMore: boolean;
  items: PlanetComment[];
};

const TAB_LABELS: Array<{ scope: TabScope; title: string; subtitle: string }> = [
  { scope: 'square', title: '星球广场', subtitle: '查看策略分享与讨论' },
  { scope: 'mine', title: '我的帖子', subtitle: '管理自己发布的内容' },
  { scope: 'favorite', title: '我的收藏', subtitle: '回看已收藏帖子' },
  { scope: 'owner', title: '互动统计', subtitle: '查看点赞、踩、收藏、评论统计' },
];

const createDefaultCommentPanel = (): CommentPanelState => ({
  open: false,
  loaded: false,
  loading: false,
  posting: false,
  deletingCommentId: null,
  draft: '',
  total: 0,
  offset: 0,
  hasMore: false,
  items: [],
});

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

const getStatusText = (status: PostStatusType) => {
  if (status === 'hidden') {
    return '隐藏';
  }
  if (status === 'deleted') {
    return '删除';
  }
  return '正常';
};

const StrategySparkline: React.FC<{ series: number[] }> = ({ series }) => {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const chartRef = useRef<ECharts | null>(null);

  useEffect(() => {
    if (!containerRef.current) {
      return;
    }

    if (!chartRef.current) {
      chartRef.current = echarts.init(containerRef.current);
    }

    const normalizedSeries = Array.isArray(series) && series.length > 0 ? series : [0];
    const option: EChartsOption = {
      animation: false,
      grid: { left: 4, right: 4, top: 6, bottom: 6 },
      xAxis: {
        type: 'category',
        show: false,
        data: normalizedSeries.map((_, idx) => idx + 1),
      },
      yAxis: {
        type: 'value',
        show: false,
        scale: true,
      },
      series: [
        {
          type: 'line',
          data: normalizedSeries,
          smooth: true,
          symbol: 'none',
          lineStyle: { width: 2, color: '#0f766e' },
          areaStyle: { color: 'rgba(15, 118, 110, 0.12)' },
        },
      ],
      tooltip: {
        trigger: 'axis',
        formatter: (params) => {
          const point = Array.isArray(params) ? params[0] : params;
          const value = typeof point?.value === 'number' ? point.value.toFixed(4) : point?.value;
          return `累计收益: ${value}`;
        },
      },
    };

    chartRef.current.setOption(option);
    const onResize = () => chartRef.current?.resize();
    window.addEventListener('resize', onResize);

    return () => {
      window.removeEventListener('resize', onResize);
    };
  }, [series]);

  useEffect(() => {
    return () => {
      chartRef.current?.dispose();
      chartRef.current = null;
    };
  }, []);

  return <div ref={containerRef} className="planet-strategy-chart" />;
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
  const actioningPostIdRef = useRef<number | null>(null);

  const [isComposerOpen, setIsComposerOpen] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [editingPostId, setEditingPostId] = useState<number | null>(null);
  const [formTitle, setFormTitle] = useState('');
  const [formContent, setFormContent] = useState('');
  const [formStatus, setFormStatus] = useState<PostStatusType>('normal');
  const [formImageUrls, setFormImageUrls] = useState<string[]>([]);
  const [formStrategyUsIds, setFormStrategyUsIds] = useState<number[]>([]);
  const [isUploadingImage, setIsUploadingImage] = useState(false);

  const [strategyOptions, setStrategyOptions] = useState<StrategyOption[]>([]);
  const [isStrategiesLoading, setIsStrategiesLoading] = useState(false);

  const [detailOpen, setDetailOpen] = useState(false);
  const [detailLoading, setDetailLoading] = useState(false);
  const [detail, setDetail] = useState<PlanetPostDetail | null>(null);

  const [expandedContents, setExpandedContents] = useState<Record<number, boolean>>({});
  const [commentPanels, setCommentPanels] = useState<Record<number, CommentPanelState>>({});
  const postsRef = useRef<PlanetPostCard[]>([]);
  const totalRef = useRef(0);

  const getCommentPanel = useCallback(
    (postId: number): CommentPanelState => commentPanels[postId] ?? createDefaultCommentPanel(),
    [commentPanels],
  );

  const patchCommentPanel = useCallback((postId: number, updater: (prev: CommentPanelState) => CommentPanelState) => {
    setCommentPanels((prev) => {
      const current = prev[postId] ?? createDefaultCommentPanel();
      return {
        ...prev,
        [postId]: updater(current),
      };
    });
  }, []);

  useEffect(() => {
    postsRef.current = posts;
  }, [posts]);

  useEffect(() => {
    totalRef.current = total;
  }, [total]);

  const applyPostListState = useCallback((nextPosts: PlanetPostCard[], nextTotal?: number) => {
    postsRef.current = nextPosts;
    setPosts(nextPosts);
    if (typeof nextTotal === 'number') {
      const normalizedTotal = Math.max(0, nextTotal);
      totalRef.current = normalizedTotal;
      setTotal(normalizedTotal);
    }
  }, []);

  const shouldIncludeInScope = useCallback((post: PlanetPostCard, scope: Exclude<TabScope, 'owner'>) => {
    if (post.status === 'deleted') {
      return false;
    }
    if (scope === 'mine') {
      return true;
    }
    if (scope === 'favorite') {
      return post.status === 'normal' && post.isFavorited;
    }
    return post.status === 'normal';
  }, []);

  const updatePostLocal = useCallback(
    (postId: number, updater: (post: PlanetPostCard) => PlanetPostCard | null) => {
      const current = postsRef.current;
      let touched = false;
      const next: PlanetPostCard[] = [];
      current.forEach((post) => {
        if (post.postId !== postId) {
          next.push(post);
          return;
        }
        touched = true;
        const updated = updater(post);
        if (updated) {
          next.push(updated);
        }
      });
      if (touched) {
        applyPostListState(next, totalRef.current + (next.length - current.length));
      }
      setDetail((prev) => {
        if (!prev || prev.post.postId !== postId) {
          return prev;
        }
        const updatedPost = updater(prev.post);
        if (!updatedPost) {
          return null;
        }
        return {
          ...prev,
          post: updatedPost,
        };
      });
      return touched;
    },
    [applyPostListState],
  );

  const upsertPostInScope = useCallback(
    (post: PlanetPostCard, scope: Exclude<TabScope, 'owner'>) => {
      const include = shouldIncludeInScope(post, scope);
      const current = postsRef.current;
      const index = current.findIndex((item) => item.postId === post.postId);
      if (include) {
        if (index >= 0) {
          const next = [...current];
          next[index] = post;
          applyPostListState(next);
        } else {
          applyPostListState([post, ...current], totalRef.current + 1);
        }
      } else if (index >= 0) {
        const next = current.filter((item) => item.postId !== post.postId);
        applyPostListState(next, totalRef.current - 1);
      }

      setDetail((prev) => {
        if (!prev || prev.post.postId !== post.postId) {
          return prev;
        }
        return {
          ...prev,
          post,
        };
      });
    },
    [applyPostListState, shouldIncludeInScope],
  );

  const resetComposer = useCallback(() => {
    setEditingPostId(null);
    setFormTitle('');
    setFormContent('');
    setFormStatus('normal');
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
        applyPostListState(Array.isArray(data?.items) ? data.items : [], data?.total ?? 0);
      } catch (err) {
        const message = err instanceof Error ? err.message : '加载帖子失败';
        showError(message);
      } finally {
        setIsLoading(false);
      }
    },
    [applyPostListState, client, showError],
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

  const fetchPostDetail = useCallback(
    async (postId: number) =>
      client.postProtocol<PlanetPostDetail, { postId: number }>(
        '/api/planet/posts/detail',
        'planet.post.detail',
        { postId },
      ),
    [client],
  );

  const loadPostDetail = useCallback(
    async (postId: number) => {
      setDetailLoading(true);
      try {
        const data = await fetchPostDetail(postId);
        setDetail(data);
      } catch (err) {
        const message = err instanceof Error ? err.message : '加载帖子详情失败';
        showError(message);
      } finally {
        setDetailLoading(false);
      }
    },
    [fetchPostDetail, showError],
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
    setFormStatus(post.status === 'deleted' ? 'normal' : post.status);
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
        status: formStatus,
        imageUrls: formImageUrls,
        strategyUsIds: formStrategyUsIds,
      };

      let targetPostId = editingPostId;
      if (editingPostId) {
        await client.postProtocol('/api/planet/posts/update', 'planet.post.update', { ...payload, postId: editingPostId });
      } else {
        const data = await client.postProtocol<PlanetPostCreateResponse>('/api/planet/posts/create', 'planet.post.create', payload);
        targetPostId = data?.postId ?? null;
      }

      showSuccess(editingPostId ? '帖子更新成功' : '帖子发布成功');
      setIsComposerOpen(false);
      resetComposer();

      if (activeTab === 'owner') {
        await loadOwnerStats();
      } else if (targetPostId && targetPostId > 0) {
        try {
          const nextDetail = await fetchPostDetail(targetPostId);
          upsertPostInScope(nextDetail.post, activeTab);
          if (detail?.post.postId === targetPostId) {
            setDetail(nextDetail);
          }
        } catch {
          await refreshCurrentTab();
        }
      } else {
        await refreshCurrentTab();
      }
    } catch (err) {
      const message = err instanceof Error ? err.message : '提交帖子失败';
      showError(message);
    } finally {
      setIsSubmitting(false);
    }
  };

  const runPostAction = async (postId: number, action: () => Promise<void>) => {
    if (actioningPostIdRef.current !== null) {
      return false;
    }
    actioningPostIdRef.current = postId;
    setActioningPostId(postId);
    try {
      await action();
      return true;
    } finally {
      actioningPostIdRef.current = null;
      setActioningPostId(null);
    }
  };

  const normalizeReaction = (reaction?: ReactionType | null): ReactionType => {
    if (reaction === 'like' || reaction === 'dislike') {
      return reaction;
    }
    return 'none';
  };

  const applyReactionPatch = (post: PlanetPostCard, nextReaction: ReactionType): PlanetPostCard => {
    const currentReaction = normalizeReaction(post.userReaction);
    let likeCount = post.likeCount;
    let dislikeCount = post.dislikeCount;
    if (currentReaction === 'like') {
      likeCount = Math.max(0, likeCount - 1);
    } else if (currentReaction === 'dislike') {
      dislikeCount = Math.max(0, dislikeCount - 1);
    }
    if (nextReaction === 'like') {
      likeCount += 1;
    } else if (nextReaction === 'dislike') {
      dislikeCount += 1;
    }
    return {
      ...post,
      userReaction: nextReaction,
      likeCount,
      dislikeCount,
    };
  };

  const handleReaction = async (post: PlanetPostCard, target: 'like' | 'dislike') => {
    const scope: Exclude<TabScope, 'owner'> = activeTab === 'owner' ? 'square' : activeTab;
    const sourcePost = postsRef.current.find((item) => item.postId === post.postId) ?? post;
    const previousPost: PlanetPostCard = {
      ...sourcePost,
      imageUrls: [...sourcePost.imageUrls],
      strategies: [...sourcePost.strategies],
    };
    const nextReaction: ReactionType = normalizeReaction(sourcePost.userReaction) === target ? 'none' : target;

    try {
      const executed = await runPostAction(post.postId, async () => {
        updatePostLocal(post.postId, (current) => applyReactionPatch(current, nextReaction));
        await client.postProtocol('/api/planet/posts/react', 'planet.post.react', {
          postId: post.postId,
          reactionType: nextReaction,
        });
      });
      if (!executed) {
        return;
      }
    } catch (err) {
      upsertPostInScope(previousPost, scope);
      const message = err instanceof Error ? err.message : '互动失败';
      showError(message);
    }
  };

  const handleFavorite = async (post: PlanetPostCard) => {
    const scope: Exclude<TabScope, 'owner'> = activeTab === 'owner' ? 'square' : activeTab;
    const sourcePost = postsRef.current.find((item) => item.postId === post.postId) ?? post;
    const previousPost: PlanetPostCard = {
      ...sourcePost,
      imageUrls: [...sourcePost.imageUrls],
      strategies: [...sourcePost.strategies],
    };
    const nextFavoriteState = !sourcePost.isFavorited;

    try {
      const executed = await runPostAction(post.postId, async () => {
        updatePostLocal(post.postId, (current) => {
          const nextCount = Math.max(
            0,
            current.favoriteCount + (nextFavoriteState === current.isFavorited ? 0 : nextFavoriteState ? 1 : -1),
          );
          const updated: PlanetPostCard = {
            ...current,
            isFavorited: nextFavoriteState,
            favoriteCount: nextCount,
          };
          if (!shouldIncludeInScope(updated, scope)) {
            return null;
          }
          return updated;
        });
        await client.postProtocol('/api/planet/posts/favorite', 'planet.post.favorite', {
          postId: post.postId,
          isFavorite: nextFavoriteState,
        });
      });
      if (!executed) {
        return;
      }
    } catch (err) {
      upsertPostInScope(previousPost, scope);
      const message = err instanceof Error ? err.message : '收藏操作失败';
      showError(message);
    }
  };

  const handlePostStatus = async (post: PlanetPostCard, nextStatus: PostStatusType) => {
    if (nextStatus === 'deleted' && !window.confirm('确认删除该帖子吗？删除后将不可见。')) {
      return;
    }

    const scope: Exclude<TabScope, 'owner'> = activeTab === 'owner' ? 'square' : activeTab;
    const sourcePost = postsRef.current.find((item) => item.postId === post.postId) ?? post;
    const previousPost: PlanetPostCard = {
      ...sourcePost,
      imageUrls: [...sourcePost.imageUrls],
      strategies: [...sourcePost.strategies],
    };

    try {
      const executed = await runPostAction(post.postId, async () => {
        updatePostLocal(post.postId, (current) => {
          const updated: PlanetPostCard = {
            ...current,
            status: nextStatus,
          };
          if (!shouldIncludeInScope(updated, scope)) {
            return null;
          }
          return updated;
        });
        await client.postProtocol('/api/planet/posts/status', 'planet.post.status.update', {
          postId: post.postId,
          status: nextStatus,
        });
      });
      if (!executed) {
        return;
      }
      if (nextStatus === 'deleted' && detail?.post.postId === post.postId) {
        closeStrategyDetail();
      }
    } catch (err) {
      upsertPostInScope(previousPost, scope);
      const message = err instanceof Error ? err.message : '状态更新失败';
      showError(message);
    }
  };

  const toggleContentExpand = (postId: number) => {
    setExpandedContents((prev) => ({
      ...prev,
      [postId]: !prev[postId],
    }));
  };

  const openStrategyDetail = async (postId: number) => {
    setDetailOpen(true);
    await loadPostDetail(postId);
  };

  const closeStrategyDetail = () => {
    setDetailOpen(false);
    setDetail(null);
  };

  const loadComments = useCallback(
    async (postId: number, reset: boolean) => {
      const panel = getCommentPanel(postId);
      const offset = reset ? 0 : panel.offset;
      const limit = reset ? 5 : 10;

      patchCommentPanel(postId, (prev) => ({ ...prev, loading: true }));
      try {
        const data = await client.postProtocol<PlanetCommentListResponse, { postId: number; offset: number; limit: number }>(
          '/api/planet/posts/comment/list',
          'planet.post.comment.list',
          { postId, offset, limit },
        );

        const incoming = Array.isArray(data?.items) ? data.items : [];
        patchCommentPanel(postId, (prev) => {
          const base = reset ? [] : prev.items;
          const merged = [...base];
          incoming.forEach((item) => {
            if (!merged.some((exist) => exist.commentId === item.commentId)) {
              merged.push(item);
            }
          });

          const totalCount = data?.total ?? merged.length;
          const hasMore = Boolean(data?.hasMore ?? merged.length < totalCount);
          return {
            ...prev,
            loaded: true,
            loading: false,
            items: merged,
            total: totalCount,
            hasMore,
            offset: merged.length,
          };
        });
      } catch (err) {
        const message = err instanceof Error ? err.message : '加载评论失败';
        showError(message);
        patchCommentPanel(postId, (prev) => ({ ...prev, loading: false }));
      }
    },
    [client, getCommentPanel, patchCommentPanel, showError],
  );

  const toggleCommentPanel = async (postId: number) => {
    const panel = getCommentPanel(postId);
    if (panel.open) {
      patchCommentPanel(postId, (prev) => ({ ...prev, open: false }));
      return;
    }

    patchCommentPanel(postId, (prev) => ({ ...prev, open: true }));
    if (!panel.loaded) {
      await loadComments(postId, true);
    }
  };

  const updateCommentDraft = (postId: number, draft: string) => {
    patchCommentPanel(postId, (prev) => ({ ...prev, draft }));
  };

  const submitComment = async (post: PlanetPostCard) => {
    const panel = getCommentPanel(post.postId);
    if (panel.posting) {
      return;
    }

    const content = panel.draft.trim();
    if (!content) {
      showError('评论内容不能为空');
      return;
    }

    patchCommentPanel(post.postId, (prev) => ({ ...prev, posting: true }));
    try {
      const createdComment = await client.postProtocol<PlanetComment | null>('/api/planet/posts/comment/create', 'planet.post.comment.create', {
        postId: post.postId,
        content,
      });

      patchCommentPanel(post.postId, (prev) => {
        const nextDraft = '';
        if (!createdComment || !createdComment.commentId) {
          return {
            ...prev,
            draft: nextDraft,
          };
        }

        if (prev.items.some((item) => item.commentId === createdComment.commentId)) {
          return {
            ...prev,
            draft: nextDraft,
          };
        }

        const nextItems = [createdComment, ...prev.items];
        const nextTotal = prev.total + 1;
        return {
          ...prev,
          draft: nextDraft,
          loaded: true,
          items: nextItems,
          total: nextTotal,
          offset: nextItems.length,
          hasMore: nextItems.length < nextTotal,
        };
      });
      if (!createdComment || !createdComment.commentId) {
        await loadComments(post.postId, true);
      }
      updatePostLocal(post.postId, (current) => ({
        ...current,
        commentCount: current.commentCount + 1,
      }));
      showSuccess('评论成功');
    } catch (err) {
      const message = err instanceof Error ? err.message : '评论失败';
      showError(message);
    } finally {
      patchCommentPanel(post.postId, (prev) => ({ ...prev, posting: false }));
    }
  };

  const deleteComment = async (postId: number, commentId: number) => {
    if (!window.confirm('确认删除该评论吗？')) {
      return;
    }

    const currentPanel = getCommentPanel(postId);
    const shouldDecreaseCount = currentPanel.items.some((item) => item.commentId === commentId);
    patchCommentPanel(postId, (prev) => ({ ...prev, deletingCommentId: commentId }));
    try {
      await client.postProtocol('/api/planet/posts/comment/delete', 'planet.post.comment.delete', { commentId });
      patchCommentPanel(postId, (prev) => {
        const nextItems = prev.items.filter((item) => item.commentId !== commentId);
        const nextTotal = shouldDecreaseCount ? Math.max(0, prev.total - 1) : prev.total;
        return {
          ...prev,
          items: nextItems,
          total: nextTotal,
          offset: nextItems.length,
          hasMore: nextItems.length < nextTotal,
        };
      });
      if (shouldDecreaseCount) {
        updatePostLocal(postId, (current) => ({
          ...current,
          commentCount: Math.max(0, current.commentCount - 1),
        }));
      }
      showSuccess('评论已删除');
    } catch (err) {
      const message = err instanceof Error ? err.message : '删除评论失败';
      showError(message);
    } finally {
      patchCommentPanel(postId, (prev) => ({ ...prev, deletingCommentId: null }));
    }
  };

  const currentTabInfo = TAB_LABELS.find((item) => item.scope === activeTab) ?? TAB_LABELS[0];

  return (
    <div className="planet-module ui-scrollable">
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
                  <span className={`planet-status-tag is-${item.status}`}>{getStatusText(item.status)}</span>
                </div>
                <div className="planet-owner-counts">
                  <span>点赞 {item.likeCount}</span>
                  <span>踩 {item.dislikeCount}</span>
                  <span>收藏 {item.favoriteCount}</span>
                  <span>评论 {item.commentCount}</span>
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
          {posts.map((post) => {
            const panel = getCommentPanel(post.postId);
            const expanded = Boolean(expandedContents[post.postId]);
            const shouldShowExpand = post.content.length > 160;

            return (
              <article key={post.postId} className="planet-post-card">
                <div className="planet-post-head">
                  {post.authorAvatarUrl ? (
                    <img className="planet-avatar" src={post.authorAvatarUrl} alt="用户头像" />
                  ) : (
                    <div className="planet-avatar planet-avatar-fallback">{post.authorName.slice(0, 1)}</div>
                  )}
                  <div className="planet-post-head-main">
                    <div className="planet-post-author">{post.authorName}</div>
                    <div className="planet-post-title-row">
                      <h3 className="planet-post-title">{post.title}</h3>
                      <span className="planet-post-time">{formatDateTime(post.createdAt)}</span>
                      <span className={`planet-status-tag is-${post.status}`}>{getStatusText(post.status)}</span>
                    </div>
                  </div>
                </div>

                <div className={`planet-post-content ${expanded ? 'is-expanded' : 'is-collapsed'}`}>{post.content}</div>
                {shouldShowExpand && (
                  <button type="button" className="planet-link-btn" onClick={() => toggleContentExpand(post.postId)}>
                    {expanded ? '收起内容' : '展开全文'}
                  </button>
                )}

                {post.imageUrls.length > 0 && (
                  <div className="planet-image-grid">
                    {post.imageUrls.map((url) => (
                      <img key={url} className="planet-post-image" src={url} alt="帖子图片" />
                    ))}
                  </div>
                )}

                {post.strategies.length > 0 && (
                  <div className="planet-strategy-board">
                    {post.strategies.map((strategy) => (
                      <div key={`${post.postId}-${strategy.usId}`} className="planet-strategy-row">
                        <div className="planet-strategy-left">
                          <div className="planet-strategy-name">{strategy.aliasName || `策略#${strategy.usId}`}</div>
                          <button type="button" className="planet-link-btn" onClick={() => openStrategyDetail(post.postId)}>
                            查看策略详情
                          </button>
                        </div>
                        <div className="planet-strategy-right">
                          <div className={`planet-curve-source ${strategy.isBacktestCurve ? 'is-backtest' : 'is-live'}`}>
                            {strategy.isBacktestCurve ? '回测数据' : '实盘数据'}
                          </div>
                          <StrategySparkline series={strategy.pnlSeries30d} />
                        </div>
                      </div>
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
                    点赞 {post.likeCount}
                  </button>
                  <button
                    type="button"
                    disabled={actioningPostId === post.postId}
                    className={post.userReaction === 'dislike' ? 'is-active' : ''}
                    onClick={() => handleReaction(post, 'dislike')}
                  >
                    踩 {post.dislikeCount}
                  </button>
                  <button
                    type="button"
                    disabled={actioningPostId === post.postId}
                    className={post.isFavorited ? 'is-active' : ''}
                    onClick={() => handleFavorite(post)}
                  >
                    收藏 {post.favoriteCount}
                  </button>
                  <button
                    type="button"
                    className={panel.open ? 'is-active' : ''}
                    disabled={actioningPostId === post.postId}
                    onClick={() => toggleCommentPanel(post.postId)}
                  >
                    评论 {post.commentCount}
                  </button>

                  {post.canManage && (
                    <>
                      {post.status !== 'normal' && (
                        <button type="button" disabled={actioningPostId === post.postId} onClick={() => handlePostStatus(post, 'normal')}>
                          设为正常
                        </button>
                      )}
                      {post.status !== 'hidden' && (
                        <button type="button" disabled={actioningPostId === post.postId} onClick={() => handlePostStatus(post, 'hidden')}>
                          设为隐藏
                        </button>
                      )}
                      {post.status !== 'deleted' && (
                        <button type="button" disabled={actioningPostId === post.postId} onClick={() => handlePostStatus(post, 'deleted')}>
                          删除帖子
                        </button>
                      )}
                      {post.status !== 'deleted' && (
                        <button type="button" disabled={actioningPostId === post.postId} onClick={() => openComposerForEdit(post)}>
                          编辑
                        </button>
                      )}
                    </>
                  )}
                </div>

                {panel.open && (
                  <div className="planet-comment-panel">
                    <div className="planet-comment-editor">
                      <textarea
                        value={panel.draft}
                        onChange={(event) => updateCommentDraft(post.postId, event.target.value)}
                        rows={3}
                        maxLength={1000}
                        placeholder="请输入评论内容"
                      />
                      <button type="button" onClick={() => submitComment(post)} disabled={panel.posting}>
                        {panel.posting ? '发送中...' : '发表评论'}
                      </button>
                    </div>

                    {panel.loading && !panel.loaded ? (
                      <div className="planet-hint">评论加载中...</div>
                    ) : panel.items.length === 0 ? (
                      <div className="planet-hint">暂无评论</div>
                    ) : (
                      <div className="planet-comment-list">
                        {panel.items.map((comment) => (
                          <div key={comment.commentId} className="planet-comment-item">
                            <div className="planet-comment-head">
                              <div className="planet-comment-author-wrap">
                                {comment.authorAvatarUrl ? (
                                  <img className="planet-comment-avatar" src={comment.authorAvatarUrl} alt="评论头像" />
                                ) : (
                                  <div className="planet-comment-avatar planet-avatar-fallback">{comment.authorName.slice(0, 1)}</div>
                                )}
                                <span className="planet-comment-author">{comment.authorName}</span>
                                <span className="planet-comment-time">{formatDateTime(comment.createdAt)}</span>
                              </div>
                              {comment.canDelete && (
                                <button
                                  type="button"
                                  className="planet-link-btn"
                                  disabled={panel.deletingCommentId === comment.commentId}
                                  onClick={() => deleteComment(post.postId, comment.commentId)}
                                >
                                  {panel.deletingCommentId === comment.commentId ? '删除中...' : '删除'}
                                </button>
                              )}
                            </div>
                            <div className="planet-comment-content">{comment.content}</div>
                          </div>
                        ))}
                      </div>
                    )}

                    {panel.hasMore && (
                      <button type="button" className="planet-more-btn" disabled={panel.loading} onClick={() => loadComments(post.postId, false)}>
                        {panel.loading ? '加载中...' : '展示更多'}
                      </button>
                    )}
                  </div>
                )}
              </article>
            );
          })}
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
            <span>帖子状态</span>
            <select value={formStatus} onChange={(event) => setFormStatus(event.target.value as PostStatusType)}>
              <option value="normal">正常</option>
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
              <div className="planet-strategy-options ui-scrollable">
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

      <Dialog open={detailOpen} onClose={closeStrategyDetail} title="策略详情" cancelText="关闭">
        {detailLoading || !detail ? (
          <div className="planet-empty">加载中...</div>
        ) : detail.strategyDetails.length === 0 ? (
          <div className="planet-empty">当前帖子未绑定策略</div>
        ) : (
          <div className="planet-detail">
            {detail.strategyDetails.map((strategy) => (
              <div key={strategy.usId} className="planet-strategy-detail-card">
                <div className="planet-strategy-detail-title">
                  {strategy.aliasName}（{strategy.defName}） · V{strategy.versionNo}
                </div>
                <div className="planet-hint">{strategy.description || '暂无策略说明'}</div>
                <details>
                  <summary>查看策略配置</summary>
                  <pre className="ui-scrollable">{strategy.configJson ? JSON.stringify(strategy.configJson, null, 2) : '暂无配置'}</pre>
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
            ))}
          </div>
        )}
      </Dialog>
    </div>
  );
};

export default PlanetModule;
