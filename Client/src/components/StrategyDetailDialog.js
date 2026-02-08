"use strict";
var __assign = (this && this.__assign) || function () {
    __assign = Object.assign || function(t) {
        for (var s, i = 1, n = arguments.length; i < n; i++) {
            s = arguments[i];
            for (var p in s) if (Object.prototype.hasOwnProperty.call(s, p))
                t[p] = s[p];
        }
        return t;
    };
    return __assign.apply(this, arguments);
};
var __awaiter = (this && this.__awaiter) || function (thisArg, _arguments, P, generator) {
    function adopt(value) { return value instanceof P ? value : new P(function (resolve) { resolve(value); }); }
    return new (P || (P = Promise))(function (resolve, reject) {
        function fulfilled(value) { try { step(generator.next(value)); } catch (e) { reject(e); } }
        function rejected(value) { try { step(generator["throw"](value)); } catch (e) { reject(e); } }
        function step(result) { result.done ? resolve(result.value) : adopt(result.value).then(fulfilled, rejected); }
        step((generator = generator.apply(thisArg, _arguments || [])).next());
    });
};
var __generator = (this && this.__generator) || function (thisArg, body) {
    var _ = { label: 0, sent: function() { if (t[0] & 1) throw t[1]; return t[1]; }, trys: [], ops: [] }, f, y, t, g = Object.create((typeof Iterator === "function" ? Iterator : Object).prototype);
    return g.next = verb(0), g["throw"] = verb(1), g["return"] = verb(2), typeof Symbol === "function" && (g[Symbol.iterator] = function() { return this; }), g;
    function verb(n) { return function (v) { return step([n, v]); }; }
    function step(op) {
        if (f) throw new TypeError("Generator is already executing.");
        while (g && (g = 0, op[0] && (_ = 0)), _) try {
            if (f = 1, y && (t = op[0] & 2 ? y["return"] : op[0] ? y["throw"] || ((t = y["return"]) && t.call(y), 0) : y.next) && !(t = t.call(y, op[1])).done) return t;
            if (y = 0, t) op = [op[0] & 2, t.value];
            switch (op[0]) {
                case 0: case 1: t = op; break;
                case 4: _.label++; return { value: op[1], done: false };
                case 5: _.label++; y = op[1]; op = [0]; continue;
                case 7: op = _.ops.pop(); _.trys.pop(); continue;
                default:
                    if (!(t = _.trys, t = t.length > 0 && t[t.length - 1]) && (op[0] === 6 || op[0] === 2)) { _ = 0; continue; }
                    if (op[0] === 3 && (!t || (op[1] > t[0] && op[1] < t[3]))) { _.label = op[1]; break; }
                    if (op[0] === 6 && _.label < t[1]) { _.label = t[1]; t = op; break; }
                    if (t && _.label < t[2]) { _.label = t[2]; _.ops.push(op); break; }
                    if (t[2]) _.ops.pop();
                    _.trys.pop(); continue;
            }
            op = body.call(thisArg, _);
        } catch (e) { op = [6, e]; y = 0; } finally { f = t = 0; }
        if (op[0] & 5) throw op[1]; return { value: op[0] ? op[1] : void 0, done: true };
    }
};
Object.defineProperty(exports, "__esModule", { value: true });
var react_1 = require("react");
var ui_1 = require("./ui");
var StrategyShareDialog_1 = require("./StrategyShareDialog");
var StrategyHistoryDialog_1 = require("./StrategyHistoryDialog");
var AlertDialog_1 = require("./AlertDialog");
var profileStore_1 = require("../auth/profileStore");
var network_1 = require("../network");
var requestId_1 = require("../network/requestId");
var httpClient_1 = require("../network/httpClient");
require("./StrategyDetailDialog.css");
var equityGranularityOptions = [
    { value: '1m', label: '1分钟' },
    { value: '15m', label: '15分钟' },
    { value: '1h', label: '1小时' },
    { value: '4h', label: '4小时' },
    { value: '1d', label: '1天' },
    { value: '3d', label: '3天' },
    { value: '7d', label: '7天' },
];
var parseJsonSafe = function (raw) {
    if (!raw) {
        return null;
    }
    try {
        return JSON.parse(raw);
    }
    catch (err) {
        console.warn('回测数据解析失败', err);
        return null;
    }
};
var LazyTable = function (_a) {
    var rawItems = _a.rawItems, parseItem = _a.parseItem, renderRow = _a.renderRow, columns = _a.columns, colSpan = _a.colSpan, emptyText = _a.emptyText, _b = _a.rowHeight, rowHeight = _b === void 0 ? 28 : _b, _c = _a.overscan, overscan = _c === void 0 ? 20 : _c;
    var items = rawItems !== null && rawItems !== void 0 ? rawItems : [];
    var containerRef = react_1.default.useRef(null);
    var cacheRef = react_1.default.useRef(new Map());
    var _d = (0, react_1.useState)(function () { return ({
        start: 0,
        end: Math.min(items.length, overscan * 2),
    }); }), range = _d[0], setRange = _d[1];
    var updateRange = react_1.default.useCallback(function () {
        var container = containerRef.current;
        if (!container) {
            return;
        }
        var scrollTop = container.scrollTop;
        var viewportHeight = container.clientHeight;
        var start = Math.max(0, Math.floor(scrollTop / rowHeight) - overscan);
        var end = Math.min(items.length, Math.ceil((scrollTop + viewportHeight) / rowHeight) + overscan);
        setRange(function (prev) { return (prev.start === start && prev.end === end ? prev : { start: start, end: end }); });
    }, [items.length, overscan, rowHeight]);
    (0, react_1.useEffect)(function () {
        cacheRef.current.clear();
        setRange({ start: 0, end: Math.min(items.length, overscan * 2) });
    }, [items.length, overscan]);
    (0, react_1.useEffect)(function () {
        var container = containerRef.current;
        if (!container) {
            return;
        }
        updateRange();
        var onScroll = function () { return updateRange(); };
        container.addEventListener('scroll', onScroll, { passive: true });
        window.addEventListener('resize', updateRange);
        return function () {
            container.removeEventListener('scroll', onScroll);
            window.removeEventListener('resize', updateRange);
        };
    }, [updateRange]);
    var visibleItems = (0, react_1.useMemo)(function () {
        if (items.length === 0) {
            return [];
        }
        var parsed = [];
        for (var i = range.start; i < range.end && i < items.length; i += 1) {
            var cached = cacheRef.current.get(i);
            if (cached) {
                parsed.push({ index: i, item: cached });
                continue;
            }
            var raw = items[i];
            var value = raw ? parseItem(raw) : null;
            if (value) {
                cacheRef.current.set(i, value);
                parsed.push({ index: i, item: value });
            }
        }
        return parsed;
    }, [items, parseItem, range.end, range.start]);
    if (items.length === 0) {
        return <div className="backtest-empty">{emptyText}</div>;
    }
    var topPad = range.start * rowHeight;
    var bottomPad = Math.max(0, items.length - range.end) * rowHeight;
    var startIndex = range.start + 1;
    var endIndex = Math.min(range.end, items.length);
    return (<>
      <div className="backtest-table-meta">
        <span>数据量：{items.length}</span>
        <span>
          解析范围：{startIndex}-{endIndex}
        </span>
      </div>
      <div className="backtest-table-wrapper" ref={containerRef}>
        <table className="backtest-table">
          <thead>{columns}</thead>
          <tbody>
            {topPad > 0 && (<tr className="backtest-table-spacer">
                <td colSpan={colSpan} style={{ height: topPad }}/>
              </tr>)}
            {visibleItems.map(function (_a) {
        var item = _a.item, index = _a.index;
        return renderRow(item, index);
    })}
            {bottomPad > 0 && (<tr className="backtest-table-spacer">
                <td colSpan={colSpan} style={{ height: bottomPad }}/>
              </tr>)}
          </tbody>
        </table>
      </div>
    </>);
};
var formatTimeframeFromSeconds = function (value) {
    if (!value || value <= 0) {
        return '';
    }
    if (value % 86400 === 0) {
        return "".concat(value / 86400, "d");
    }
    if (value % 3600 === 0) {
        return "".concat(value / 3600, "h");
    }
    if (value % 60 === 0) {
        return "".concat(value / 60, "m");
    }
    return "".concat(value, "s");
};
var buildBacktestDefaults = function (config) {
    var _a, _b, _c, _d, _e, _f;
    var trade = config === null || config === void 0 ? void 0 : config.trade;
    return {
        exchange: (_a = trade === null || trade === void 0 ? void 0 : trade.exchange) !== null && _a !== void 0 ? _a : '',
        symbols: (_b = trade === null || trade === void 0 ? void 0 : trade.symbol) !== null && _b !== void 0 ? _b : '',
        timeframe: formatTimeframeFromSeconds(trade === null || trade === void 0 ? void 0 : trade.timeframeSec),
        rangeMode: 'bars',
        startTime: '',
        endTime: '',
        barCount: '1000',
        initialCapital: '10000',
        orderQty: ((_c = trade === null || trade === void 0 ? void 0 : trade.sizing) === null || _c === void 0 ? void 0 : _c.orderQty) !== undefined ? String(trade.sizing.orderQty) : '',
        leverage: ((_d = trade === null || trade === void 0 ? void 0 : trade.sizing) === null || _d === void 0 ? void 0 : _d.leverage) !== undefined ? String(trade.sizing.leverage) : '',
        takeProfitPct: ((_e = trade === null || trade === void 0 ? void 0 : trade.risk) === null || _e === void 0 ? void 0 : _e.takeProfitPct) !== undefined ? String(trade.risk.takeProfitPct) : '',
        stopLossPct: ((_f = trade === null || trade === void 0 ? void 0 : trade.risk) === null || _f === void 0 ? void 0 : _f.stopLossPct) !== undefined ? String(trade.risk.stopLossPct) : '',
        feeRate: '0.0004',
        fundingRate: '0',
        slippageBps: '0',
        autoReverse: false,
        useStrategyRuntime: true,
        outputTrades: true,
        outputEquity: true,
        outputEvents: true,
        outputEquityGranularity: '1m',
    };
};
var StrategyDetailDialog = function (_a) {
    var _b, _c, _d, _e, _f;
    var strategy = _a.strategy, onClose = _a.onClose, onCreateVersion = _a.onCreateVersion, onViewHistory = _a.onViewHistory, onCreateShare = _a.onCreateShare, onUpdateStatus = _a.onUpdateStatus, onDelete = _a.onDelete, onEditStrategy = _a.onEditStrategy, onFetchOpenPositionsCount = _a.onFetchOpenPositionsCount, onFetchPositions = _a.onFetchPositions, onClosePositions = _a.onClosePositions, onClosePosition = _a.onClosePosition, onPublishOfficial = _a.onPublishOfficial, onPublishTemplate = _a.onPublishTemplate, onPublishMarket = _a.onPublishMarket, onSyncOfficial = _a.onSyncOfficial, onSyncTemplate = _a.onSyncTemplate, onSyncMarket = _a.onSyncMarket, onRemoveOfficial = _a.onRemoveOfficial, onRemoveTemplate = _a.onRemoveTemplate, onRunBacktest = _a.onRunBacktest;
    var _g = (0, ui_1.useNotification)(), success = _g.success, error = _g.error;
    var _h = (0, react_1.useState)('info'), activeTab = _h[0], setActiveTab = _h[1];
    var _j = (0, react_1.useState)('completed'), currentStatus = _j[0], setCurrentStatus = _j[1];
    var _k = (0, react_1.useState)(false), isUpdatingStatus = _k[0], setIsUpdatingStatus = _k[1];
    var _l = (0, react_1.useState)([]), historyVersions = _l[0], setHistoryVersions = _l[1];
    var _m = (0, react_1.useState)(null), selectedHistoryVersionId = _m[0], setSelectedHistoryVersionId = _m[1];
    var _o = (0, react_1.useState)(false), isHistoryLoading = _o[0], setIsHistoryLoading = _o[1];
    var _p = (0, react_1.useState)(null), shareCode = _p[0], setShareCode = _p[1];
    var _q = (0, react_1.useState)(false), isShareLoading = _q[0], setIsShareLoading = _q[1];
    var _r = (0, react_1.useState)(null), publishTarget = _r[0], setPublishTarget = _r[1];
    var _s = (0, react_1.useState)(false), isPublishing = _s[0], setIsPublishing = _s[1];
    var _t = (0, react_1.useState)(false), isMarketPublishing = _t[0], setIsMarketPublishing = _t[1];
    var _u = (0, react_1.useState)(false), isMarketConfirmOpen = _u[0], setIsMarketConfirmOpen = _u[1];
    var _v = (0, react_1.useState)(false), isEditConfirmOpen = _v[0], setIsEditConfirmOpen = _v[1];
    var _w = (0, react_1.useState)(0), openPositionCount = _w[0], setOpenPositionCount = _w[1];
    var _x = (0, react_1.useState)(false), isCheckingPositions = _x[0], setIsCheckingPositions = _x[1];
    var _y = (0, react_1.useState)(null), syncTarget = _y[0], setSyncTarget = _y[1];
    var _z = (0, react_1.useState)(false), isSyncing = _z[0], setIsSyncing = _z[1];
    var _0 = (0, react_1.useState)(null), removeTarget = _0[0], setRemoveTarget = _0[1];
    var _1 = (0, react_1.useState)(false), isRemoving = _1[0], setIsRemoving = _1[1];
    var _2 = (0, react_1.useState)([]), positions = _2[0], setPositions = _2[1];
    var _3 = (0, react_1.useState)(false), isPositionsLoading = _3[0], setIsPositionsLoading = _3[1];
    var _4 = (0, react_1.useState)(false), hasLoadedPositions = _4[0], setHasLoadedPositions = _4[1];
    var _5 = (0, react_1.useState)(false), isClosePositionsConfirmOpen = _5[0], setIsClosePositionsConfirmOpen = _5[1];
    var _6 = (0, react_1.useState)(false), isClosingPositions = _6[0], setIsClosingPositions = _6[1];
    var _7 = (0, react_1.useState)(null), closePositionTarget = _7[0], setClosePositionTarget = _7[1];
    var _8 = (0, react_1.useState)(false), isClosingPosition = _8[0], setIsClosingPosition = _8[1];
    var _9 = (0, react_1.useState)(function () { return buildBacktestDefaults(null); }), backtestForm = _9[0], setBacktestForm = _9[1];
    var _10 = (0, react_1.useState)(null), backtestResult = _10[0], setBacktestResult = _10[1];
    var _11 = (0, react_1.useState)(null), backtestError = _11[0], setBacktestError = _11[1];
    var _12 = (0, react_1.useState)(false), isBacktestRunning = _12[0], setIsBacktestRunning = _12[1];
    var _13 = (0, react_1.useState)(''), backtestProgressStageCode = _13[0], setBacktestProgressStageCode = _13[1];
    var _14 = (0, react_1.useState)(''), backtestProgressStage = _14[0], setBacktestProgressStage = _14[1];
    var _15 = (0, react_1.useState)(''), backtestProgressMessage = _15[0], setBacktestProgressMessage = _15[1];
    var _16 = (0, react_1.useState)(null), backtestProgressPercent = _16[0], setBacktestProgressPercent = _16[1];
    var _17 = (0, react_1.useState)(0), backtestFoundPositions = _17[0], setBacktestFoundPositions = _17[1];
    var _18 = (0, react_1.useState)(0), backtestTotalPositions = _18[0], setBacktestTotalPositions = _18[1];
    var _19 = (0, react_1.useState)(0), backtestWinCount = _19[0], setBacktestWinCount = _19[1];
    var _20 = (0, react_1.useState)(0), backtestLossCount = _20[0], setBacktestLossCount = _20[1];
    var _21 = (0, react_1.useState)(null), backtestWinRate = _21[0], setBacktestWinRate = _21[1];
    var _22 = (0, react_1.useState)([]), backtestStreamingTrades = _22[0], setBacktestStreamingTrades = _22[1];
    var _23 = (0, react_1.useState)([]), cacheSnapshots = _23[0], setCacheSnapshots = _23[1];
    var _24 = (0, react_1.useState)(false), isLoadingCache = _24[0], setIsLoadingCache = _24[1];
    var httpClient = (0, react_1.useMemo)(function () { return new httpClient_1.HttpClient({ tokenProvider: network_1.getToken }); }, []);
    var profile = (0, react_1.useMemo)(function () { return (0, profileStore_1.getAuthProfile)(); }, []);
    var canPublish = (profile === null || profile === void 0 ? void 0 : profile.role) === 255;
    var resolvedConfig = (0, react_1.useMemo)(function () {
        if (!(strategy === null || strategy === void 0 ? void 0 : strategy.configJson)) {
            return null;
        }
        if (typeof strategy.configJson === 'string') {
            try {
                return JSON.parse(strategy.configJson);
            }
            catch (_a) {
                return null;
            }
        }
        return strategy.configJson;
    }, [strategy === null || strategy === void 0 ? void 0 : strategy.configJson]);
    var defaultBacktestForm = (0, react_1.useMemo)(function () { return buildBacktestDefaults(resolvedConfig); }, [resolvedConfig]);
    (0, react_1.useEffect)(function () {
        var _a;
        if (strategy) {
            var status_1 = (_a = strategy.state) === null || _a === void 0 ? void 0 : _a.trim().toLowerCase();
            if (status_1 === 'running') {
                setCurrentStatus('running');
            }
            else if (status_1 === 'paused') {
                setCurrentStatus('paused');
            }
            else if (status_1 === 'paused_open_position') {
                setCurrentStatus('paused_open_position');
            }
            else {
                setCurrentStatus('completed');
            }
        }
    }, [strategy]);
    // 加载历史行情缓存数据
    (0, react_1.useEffect)(function () {
        if (activeTab !== 'backtest') {
            return;
        }
        loadCacheSnapshots();
    }, [activeTab]);
    var loadCacheSnapshots = function () { return __awaiter(void 0, void 0, void 0, function () {
        var response, err_1;
        return __generator(this, function (_a) {
            switch (_a.label) {
                case 0:
                    setIsLoadingCache(true);
                    _a.label = 1;
                case 1:
                    _a.trys.push([1, 3, 4, 5]);
                    return [4 /*yield*/, httpClient.postProtocol('/api/MarketData/cache-snapshots', 'marketdata.cache.snapshots', {})];
                case 2:
                    response = _a.sent();
                    setCacheSnapshots(response.snapshots || []);
                    return [3 /*break*/, 5];
                case 3:
                    err_1 = _a.sent();
                    console.error('加载缓存数据失败', err_1);
                    return [3 /*break*/, 5];
                case 4:
                    setIsLoadingCache(false);
                    return [7 /*endfinally*/];
                case 5: return [2 /*return*/];
            }
        });
    }); };
    // 获取可用的交易所列表
    var availableExchanges = (0, react_1.useMemo)(function () {
        var exchanges = new Set();
        cacheSnapshots.forEach(function (snapshot) {
            exchanges.add(snapshot.exchange);
        });
        return Array.from(exchanges).sort();
    }, [cacheSnapshots]);
    // 根据选择的交易所获取可用的币种
    var availableSymbols = (0, react_1.useMemo)(function () {
        if (!backtestForm.exchange) {
            return [];
        }
        var symbols = new Set();
        cacheSnapshots.forEach(function (snapshot) {
            if (snapshot.exchange === backtestForm.exchange) {
                symbols.add(snapshot.symbol);
            }
        });
        return Array.from(symbols).sort();
    }, [cacheSnapshots, backtestForm.exchange]);
    // 根据选择的交易所和币种获取可用的周期
    var availableTimeframes = (0, react_1.useMemo)(function () {
        if (!backtestForm.exchange || !backtestForm.symbols) {
            return [];
        }
        var symbols = backtestForm.symbols.split(/[,\s]+/).filter(Boolean);
        if (symbols.length === 0) {
            return [];
        }
        var timeframes = new Set();
        cacheSnapshots.forEach(function (snapshot) {
            if (snapshot.exchange === backtestForm.exchange && symbols.includes(snapshot.symbol)) {
                timeframes.add(snapshot.timeframe);
            }
        });
        return Array.from(timeframes).sort();
    }, [cacheSnapshots, backtestForm.exchange, backtestForm.symbols]);
    // 获取支持的时间范围（取交集）
    var supportedTimeRange = (0, react_1.useMemo)(function () {
        if (!backtestForm.exchange || !backtestForm.symbols || !backtestForm.timeframe) {
            return null;
        }
        var symbols = backtestForm.symbols.split(/[,\s]+/).filter(Boolean);
        if (symbols.length === 0) {
            return null;
        }
        var earliestStart = null;
        var latestEnd = null;
        var foundAny = false;
        cacheSnapshots.forEach(function (snapshot) {
            if (snapshot.exchange === backtestForm.exchange &&
                symbols.includes(snapshot.symbol) &&
                snapshot.timeframe === backtestForm.timeframe) {
                foundAny = true;
                var start = new Date(snapshot.startTime);
                var end = new Date(snapshot.endTime);
                if (!earliestStart || start > earliestStart) {
                    earliestStart = start;
                }
                if (!latestEnd || end < latestEnd) {
                    latestEnd = end;
                }
            }
        });
        if (!foundAny || !earliestStart || !latestEnd) {
            return null;
        }
        return {
            start: earliestStart,
            end: latestEnd,
        };
    }, [cacheSnapshots, backtestForm.exchange, backtestForm.symbols, backtestForm.timeframe]);
    // 全量回测功能
    var handleFullRangeBacktest = function () {
        if (!supportedTimeRange) {
            error('请先选择交易所、币种和周期');
            return;
        }
        // 切换到时间范围模式
        updateBacktestField('rangeMode', 'time');
        // 设置时间范围
        var startDate = new Date(supportedTimeRange.start);
        var endDate = new Date(supportedTimeRange.end);
        // 转换为本地时间格式 (YYYY-MM-DDTHH:mm)
        var formatLocalDateTime = function (date) {
            var year = date.getFullYear();
            var month = String(date.getMonth() + 1).padStart(2, '0');
            var day = String(date.getDate()).padStart(2, '0');
            var hours = String(date.getHours()).padStart(2, '0');
            var minutes = String(date.getMinutes()).padStart(2, '0');
            return "".concat(year, "-").concat(month, "-").concat(day, "T").concat(hours, ":").concat(minutes);
        };
        updateBacktestField('startTime', formatLocalDateTime(startDate));
        updateBacktestField('endTime', formatLocalDateTime(endDate));
        success('已设置为全量回测时间范围');
    };
    (0, react_1.useEffect)(function () {
        if (!strategy) {
            return;
        }
        setBacktestForm(defaultBacktestForm);
        setBacktestResult(null);
        setBacktestError(null);
        setBacktestProgressStageCode('');
        setBacktestProgressStage('');
        setBacktestProgressMessage('');
        setBacktestProgressPercent(null);
        setBacktestFoundPositions(0);
        setBacktestTotalPositions(0);
        setBacktestWinCount(0);
        setBacktestLossCount(0);
        setBacktestWinRate(null);
        setBacktestStreamingTrades([]);
    }, [defaultBacktestForm, strategy === null || strategy === void 0 ? void 0 : strategy.usId]);
    var handleUpdateStatus = function (newStatus) { return __awaiter(void 0, void 0, void 0, function () {
        var err_2, message;
        return __generator(this, function (_a) {
            switch (_a.label) {
                case 0:
                    if (!strategy || isUpdatingStatus) {
                        return [2 /*return*/];
                    }
                    setIsUpdatingStatus(true);
                    _a.label = 1;
                case 1:
                    _a.trys.push([1, 3, 4, 5]);
                    return [4 /*yield*/, onUpdateStatus(strategy.usId, newStatus)];
                case 2:
                    _a.sent();
                    setCurrentStatus(newStatus);
                    success('策略状态已更新');
                    return [3 /*break*/, 5];
                case 3:
                    err_2 = _a.sent();
                    message = err_2 instanceof Error ? err_2.message : '更新策略状态失败';
                    error(message);
                    return [3 /*break*/, 5];
                case 4:
                    setIsUpdatingStatus(false);
                    return [7 /*endfinally*/];
                case 5: return [2 /*return*/];
            }
        });
    }); };
    var handleLoadHistory = function () { return __awaiter(void 0, void 0, void 0, function () {
        var versions, pinnedVersion, fallbackVersion, err_3, message;
        return __generator(this, function (_a) {
            switch (_a.label) {
                case 0:
                    if (!strategy || isHistoryLoading) {
                        return [2 /*return*/];
                    }
                    setIsHistoryLoading(true);
                    _a.label = 1;
                case 1:
                    _a.trys.push([1, 3, 4, 5]);
                    return [4 /*yield*/, onViewHistory(strategy.usId)];
                case 2:
                    versions = _a.sent();
                    setHistoryVersions(versions);
                    pinnedVersion = versions.find(function (item) { return item.isPinned; });
                    fallbackVersion = pinnedVersion !== null && pinnedVersion !== void 0 ? pinnedVersion : versions[versions.length - 1];
                    setSelectedHistoryVersionId(fallbackVersion ? fallbackVersion.versionId : null);
                    return [3 /*break*/, 5];
                case 3:
                    err_3 = _a.sent();
                    message = err_3 instanceof Error ? err_3.message : '加载历史版本失败';
                    error(message);
                    return [3 /*break*/, 5];
                case 4:
                    setIsHistoryLoading(false);
                    return [7 /*endfinally*/];
                case 5: return [2 /*return*/];
            }
        });
    }); };
    var handleCreateShare = function (payload) { return __awaiter(void 0, void 0, void 0, function () {
        var code;
        return __generator(this, function (_a) {
            switch (_a.label) {
                case 0:
                    if (!strategy || isShareLoading) {
                        throw new Error('策略未选择');
                    }
                    setIsShareLoading(true);
                    _a.label = 1;
                case 1:
                    _a.trys.push([1, , 3, 4]);
                    return [4 /*yield*/, onCreateShare(strategy.usId, payload)];
                case 2:
                    code = _a.sent();
                    setShareCode(code);
                    return [2 /*return*/, code];
                case 3:
                    setIsShareLoading(false);
                    return [7 /*endfinally*/];
                case 4: return [2 /*return*/];
            }
        });
    }); };
    var handleTabChange = function (tab) {
        setActiveTab(tab);
        if (tab === 'history' && historyVersions.length === 0) {
            handleLoadHistory();
        }
        if (tab === 'positions' && !hasLoadedPositions) {
            handleLoadPositions(false);
        }
    };
    var handleCreateVersion = function () {
        if (strategy) {
            onCreateVersion(strategy.usId);
            onClose();
        }
    };
    var handleEditStrategy = function () { return __awaiter(void 0, void 0, void 0, function () {
        var count, err_4, message;
        return __generator(this, function (_a) {
            switch (_a.label) {
                case 0:
                    if (!strategy || isCheckingPositions) {
                        return [2 /*return*/];
                    }
                    setIsCheckingPositions(true);
                    _a.label = 1;
                case 1:
                    _a.trys.push([1, 3, 4, 5]);
                    return [4 /*yield*/, onFetchOpenPositionsCount(strategy.usId)];
                case 2:
                    count = _a.sent();
                    if (count > 0) {
                        setOpenPositionCount(count);
                        setIsEditConfirmOpen(true);
                        return [2 /*return*/];
                    }
                    onEditStrategy(strategy.usId);
                    onClose();
                    return [3 /*break*/, 5];
                case 3:
                    err_4 = _a.sent();
                    message = err_4 instanceof Error ? err_4.message : '获取仓位信息失败';
                    error(message);
                    return [3 /*break*/, 5];
                case 4:
                    setIsCheckingPositions(false);
                    return [7 /*endfinally*/];
                case 5: return [2 /*return*/];
            }
        });
    }); };
    var handleCloseAllPositions = function () { return __awaiter(void 0, void 0, void 0, function () {
        var err_5, message;
        return __generator(this, function (_a) {
            switch (_a.label) {
                case 0:
                    if (!strategy || isClosingPositions) {
                        return [2 /*return*/];
                    }
                    setIsClosingPositions(true);
                    _a.label = 1;
                case 1:
                    _a.trys.push([1, 5, 6, 7]);
                    return [4 /*yield*/, onUpdateStatus(strategy.usId, 'paused')];
                case 2:
                    _a.sent();
                    return [4 /*yield*/, onClosePositions(strategy.usId)];
                case 3:
                    _a.sent();
                    success('已发起一键平仓');
                    return [4 /*yield*/, handleLoadPositions(true)];
                case 4:
                    _a.sent();
                    return [3 /*break*/, 7];
                case 5:
                    err_5 = _a.sent();
                    message = err_5 instanceof Error ? err_5.message : '一键平仓失败';
                    error(message);
                    return [3 /*break*/, 7];
                case 6:
                    setIsClosingPositions(false);
                    setIsClosePositionsConfirmOpen(false);
                    return [7 /*endfinally*/];
                case 7: return [2 /*return*/];
            }
        });
    }); };
    var handleClosePosition = function () { return __awaiter(void 0, void 0, void 0, function () {
        var err_6, message;
        return __generator(this, function (_a) {
            switch (_a.label) {
                case 0:
                    if (!closePositionTarget || isClosingPosition) {
                        return [2 /*return*/];
                    }
                    if (!closePositionTarget.positionId) {
                        error('仓位ID无效');
                        setClosePositionTarget(null);
                        return [2 /*return*/];
                    }
                    setIsClosingPosition(true);
                    _a.label = 1;
                case 1:
                    _a.trys.push([1, 4, 5, 6]);
                    return [4 /*yield*/, onClosePosition(closePositionTarget.positionId)];
                case 2:
                    _a.sent();
                    success('已发起平仓');
                    return [4 /*yield*/, handleLoadPositions(true)];
                case 3:
                    _a.sent();
                    return [3 /*break*/, 6];
                case 4:
                    err_6 = _a.sent();
                    message = err_6 instanceof Error ? err_6.message : '平仓失败';
                    error(message);
                    return [3 /*break*/, 6];
                case 5:
                    setIsClosingPosition(false);
                    setClosePositionTarget(null);
                    return [7 /*endfinally*/];
                case 6: return [2 /*return*/];
            }
        });
    }); };
    var handleLoadPositions = function (forceReload) { return __awaiter(void 0, void 0, void 0, function () {
        var items, err_7, message;
        return __generator(this, function (_a) {
            switch (_a.label) {
                case 0:
                    if (!strategy || isPositionsLoading) {
                        return [2 /*return*/];
                    }
                    if (!forceReload && hasLoadedPositions) {
                        return [2 /*return*/];
                    }
                    setIsPositionsLoading(true);
                    _a.label = 1;
                case 1:
                    _a.trys.push([1, 3, 4, 5]);
                    return [4 /*yield*/, onFetchPositions(strategy.usId)];
                case 2:
                    items = _a.sent();
                    setPositions(items);
                    setHasLoadedPositions(true);
                    return [3 /*break*/, 5];
                case 3:
                    err_7 = _a.sent();
                    message = err_7 instanceof Error ? err_7.message : '获取仓位历史失败';
                    error(message);
                    return [3 /*break*/, 5];
                case 4:
                    setIsPositionsLoading(false);
                    return [7 /*endfinally*/];
                case 5: return [2 /*return*/];
            }
        });
    }); };
    var handlePublish = function (target) { return __awaiter(void 0, void 0, void 0, function () {
        var err_8, message;
        return __generator(this, function (_a) {
            switch (_a.label) {
                case 0:
                    if (!strategy || isPublishing) {
                        return [2 /*return*/];
                    }
                    setIsPublishing(true);
                    _a.label = 1;
                case 1:
                    _a.trys.push([1, 6, 7, 8]);
                    if (!(target === 'official')) return [3 /*break*/, 3];
                    return [4 /*yield*/, onPublishOfficial(strategy.usId)];
                case 2:
                    _a.sent();
                    success('已发布到官方策略库');
                    return [3 /*break*/, 5];
                case 3: return [4 /*yield*/, onPublishTemplate(strategy.usId)];
                case 4:
                    _a.sent();
                    success('已发布到策略模板库');
                    _a.label = 5;
                case 5: return [3 /*break*/, 8];
                case 6:
                    err_8 = _a.sent();
                    message = err_8 instanceof Error ? err_8.message : '发布失败';
                    error(message);
                    return [3 /*break*/, 8];
                case 7:
                    setIsPublishing(false);
                    setPublishTarget(null);
                    return [7 /*endfinally*/];
                case 8: return [2 /*return*/];
            }
        });
    }); };
    var handlePublishMarket = function () { return __awaiter(void 0, void 0, void 0, function () {
        var err_9, message;
        return __generator(this, function (_a) {
            switch (_a.label) {
                case 0:
                    if (!strategy || isMarketPublishing) {
                        return [2 /*return*/];
                    }
                    setIsMarketPublishing(true);
                    _a.label = 1;
                case 1:
                    _a.trys.push([1, 3, 4, 5]);
                    return [4 /*yield*/, onPublishMarket(strategy.usId)];
                case 2:
                    _a.sent();
                    success('已公开到策略广场');
                    return [3 /*break*/, 5];
                case 3:
                    err_9 = _a.sent();
                    message = err_9 instanceof Error ? err_9.message : '公开失败，请稍后重试';
                    error(message);
                    return [3 /*break*/, 5];
                case 4:
                    setIsMarketPublishing(false);
                    setIsMarketConfirmOpen(false);
                    return [7 /*endfinally*/];
                case 5: return [2 /*return*/];
            }
        });
    }); };
    var handleSync = function (target) { return __awaiter(void 0, void 0, void 0, function () {
        var err_10, message;
        return __generator(this, function (_a) {
            switch (_a.label) {
                case 0:
                    if (!strategy || isSyncing) {
                        return [2 /*return*/];
                    }
                    setIsSyncing(true);
                    _a.label = 1;
                case 1:
                    _a.trys.push([1, 8, 9, 10]);
                    if (!(target === 'official')) return [3 /*break*/, 3];
                    return [4 /*yield*/, onSyncOfficial(strategy.usId)];
                case 2:
                    _a.sent();
                    success('已发布最新版本到官方策略库');
                    return [3 /*break*/, 7];
                case 3:
                    if (!(target === 'template')) return [3 /*break*/, 5];
                    return [4 /*yield*/, onSyncTemplate(strategy.usId)];
                case 4:
                    _a.sent();
                    success('已发布最新版本到策略模板库');
                    return [3 /*break*/, 7];
                case 5: return [4 /*yield*/, onSyncMarket(strategy.usId)];
                case 6:
                    _a.sent();
                    success('已发布最新版本到策略广场');
                    _a.label = 7;
                case 7: return [3 /*break*/, 10];
                case 8:
                    err_10 = _a.sent();
                    message = err_10 instanceof Error ? err_10.message : '发布最新版本失败';
                    error(message);
                    return [3 /*break*/, 10];
                case 9:
                    setIsSyncing(false);
                    setSyncTarget(null);
                    return [7 /*endfinally*/];
                case 10: return [2 /*return*/];
            }
        });
    }); };
    var handleRemove = function (target) { return __awaiter(void 0, void 0, void 0, function () {
        var err_11, message;
        return __generator(this, function (_a) {
            switch (_a.label) {
                case 0:
                    if (!strategy || isRemoving) {
                        return [2 /*return*/];
                    }
                    setIsRemoving(true);
                    _a.label = 1;
                case 1:
                    _a.trys.push([1, 6, 7, 8]);
                    if (!(target === 'official')) return [3 /*break*/, 3];
                    return [4 /*yield*/, onRemoveOfficial(strategy.usId)];
                case 2:
                    _a.sent();
                    success('已从官方策略库移除');
                    return [3 /*break*/, 5];
                case 3: return [4 /*yield*/, onRemoveTemplate(strategy.usId)];
                case 4:
                    _a.sent();
                    success('已从策略模板库移除');
                    _a.label = 5;
                case 5: return [3 /*break*/, 8];
                case 6:
                    err_11 = _a.sent();
                    message = err_11 instanceof Error ? err_11.message : '移除失败';
                    error(message);
                    return [3 /*break*/, 8];
                case 7:
                    setIsRemoving(false);
                    setRemoveTarget(null);
                    return [7 /*endfinally*/];
                case 8: return [2 /*return*/];
            }
        });
    }); };
    var getStatusText = function (status) {
        switch (status) {
            case 'running':
                return '运行中';
            case 'paused':
                return '已暂停';
            case 'paused_open_position':
                return '暂停开新仓';
            case 'completed':
                return '完成';
            case 'error':
                return '错误';
            default:
                return '完成';
        }
    };
    var getStatusColor = function (status) {
        switch (status) {
            case 'running':
                return 'status-running';
            case 'paused':
                return 'status-paused';
            case 'paused_open_position':
                return 'status-paused-open-position';
            case 'completed':
                return 'status-completed';
            case 'error':
                return 'status-error';
            default:
                return 'status-completed';
        }
    };
    var formatNumber = function (value) {
        if (value === null || value === undefined || Number.isNaN(value)) {
            return '-';
        }
        return value.toFixed(4).replace(/\.?0+$/, '');
    };
    var formatStatus = function (status, closeReason) {
        if (!status) {
            return '-';
        }
        var normalized = status.toLowerCase();
        if (normalized === 'open') {
            return '未平仓';
        }
        if (normalized === 'closed') {
            var reasonText = formatCloseReason(closeReason);
            return reasonText !== '-' ? reasonText : '已平仓';
        }
        return status;
    };
    var formatSide = function (side) {
        if (!side) {
            return '-';
        }
        var normalized = side.toLowerCase();
        if (normalized === 'long') {
            return '多';
        }
        if (normalized === 'short') {
            return '空';
        }
        return side;
    };
    var formatBoolean = function (value) {
        if (value === null || value === undefined) {
            return '-';
        }
        return value ? '是' : '否';
    };
    var formatCloseReason = function (value) {
        if (!value) {
            return '-';
        }
        var normalized = value.toLowerCase();
        if (normalized === 'manualsingle' || normalized === 'manual_single') {
            return '手动平此仓';
        }
        if (normalized === 'manualbatch' || normalized === 'manual_batch') {
            return '批量一键平仓';
        }
        if (normalized === 'manual') {
            return '手动平仓';
        }
        if (normalized === 'stoploss') {
            return '固定止损';
        }
        if (normalized === 'takeprofit') {
            return '固定止盈';
        }
        if (normalized === 'trailingstop') {
            return '移动止盈止损';
        }
        return value;
    };
    var formatDateTimeLocal = function (value) {
        if (!value) {
            return '-';
        }
        var date = new Date(value);
        if (Number.isNaN(date.getTime())) {
            return value;
        }
        return date.toLocaleString('zh-CN', {
            year: 'numeric',
            month: '2-digit',
            day: '2-digit',
            hour: '2-digit',
            minute: '2-digit',
            second: '2-digit',
            hour12: false,
        });
    };
    var formatPercent = function (value) {
        if (value === null || value === undefined || Number.isNaN(value)) {
            return '-';
        }
        var normalized = value * 100;
        return "".concat(normalized.toFixed(2).replace(/\.00$/, ''), "%");
    };
    var formatTimestamp = function (value) {
        if (!value) {
            return '-';
        }
        var date = new Date(value);
        if (Number.isNaN(date.getTime())) {
            return String(value);
        }
        return date.toLocaleString('zh-CN', {
            year: 'numeric',
            month: '2-digit',
            day: '2-digit',
            hour: '2-digit',
            minute: '2-digit',
            second: '2-digit',
            hour12: false,
        });
    };
    var formatDuration = function (value) {
        if (value === null || value === undefined || Number.isNaN(value)) {
            return '-';
        }
        if (value < 1000) {
            return "".concat(value, " ms");
        }
        var seconds = value / 1000;
        if (seconds < 60) {
            return "".concat(seconds.toFixed(2).replace(/\.00$/, ''), " s");
        }
        var minutes = Math.floor(seconds / 60);
        var remain = seconds % 60;
        return "".concat(minutes, " \u5206 ").concat(remain.toFixed(1).replace(/\.0$/, ''), " \u79D2");
    };
    var parseSymbols = function (value) {
        var items = value
            .split(/[,，\s]+/)
            .map(function (item) { return item.trim(); })
            .filter(function (item) { return item.length > 0; });
        return Array.from(new Set(items));
    };
    var toServerDateTime = function (value) {
        var trimmed = value.trim();
        if (!trimmed) {
            return null;
        }
        // 使用本地时间字符串，确保后端解析格式一致
        if (trimmed.includes('T')) {
            var replaced = trimmed.replace('T', ' ');
            return replaced.length === 16 ? "".concat(replaced, ":00") : replaced;
        }
        return trimmed;
    };
    var updateBacktestField = function (field, value) {
        setBacktestForm(function (prev) {
            var _a;
            return (__assign(__assign({}, prev), (_a = {}, _a[field] = value, _a)));
        });
    };
    var handleResetBacktest = function () {
        setBacktestForm(defaultBacktestForm);
        setBacktestResult(null);
        setBacktestError(null);
        setBacktestProgressStageCode('');
        setBacktestProgressStage('');
        setBacktestProgressMessage('');
        setBacktestProgressPercent(null);
        setBacktestFoundPositions(0);
        setBacktestTotalPositions(0);
        setBacktestWinCount(0);
        setBacktestLossCount(0);
        setBacktestWinRate(null);
        setBacktestStreamingTrades([]);
    };
    var handleRunBacktest = function () { return __awaiter(void 0, void 0, void 0, function () {
        var parseNumberValue, errors, exchange, timeframe, symbols, initialCapitalResult, feeRateResult, fundingRateResult, slippageResult, orderQtyResult, leverageResult, takeProfitResult, stopLossResult, startTime, endTime, barCount, barCountResult, message, payload, reqId, ws, unsubscribeProgress, result, err_12, message;
        var _a, _b, _c;
        return __generator(this, function (_d) {
            switch (_d.label) {
                case 0:
                    if (!strategy || isBacktestRunning) {
                        return [2 /*return*/];
                    }
                    parseNumberValue = function (raw, label, options) {
                        var trimmed = raw.trim();
                        if (!trimmed) {
                            if (options === null || options === void 0 ? void 0 : options.required) {
                                return { value: null, error: "".concat(label, "\u4E0D\u80FD\u4E3A\u7A7A") };
                            }
                            return { value: null };
                        }
                        var parsed = Number(trimmed);
                        if (Number.isNaN(parsed)) {
                            return { value: null, error: "".concat(label, "\u683C\u5F0F\u4E0D\u6B63\u786E") };
                        }
                        if ((options === null || options === void 0 ? void 0 : options.integer) && !Number.isInteger(parsed)) {
                            return { value: null, error: "".concat(label, "\u5FC5\u987B\u4E3A\u6574\u6570") };
                        }
                        if ((options === null || options === void 0 ? void 0 : options.min) !== undefined && parsed < options.min) {
                            return { value: null, error: "".concat(label, "\u5FC5\u987B\u5927\u4E8E\u7B49\u4E8E ").concat(options.min) };
                        }
                        return { value: parsed };
                    };
                    errors = [];
                    exchange = backtestForm.exchange.trim();
                    timeframe = backtestForm.timeframe.trim();
                    symbols = parseSymbols(backtestForm.symbols);
                    initialCapitalResult = parseNumberValue(backtestForm.initialCapital, '初始资金', { required: true, min: 0 });
                    if (initialCapitalResult.error) {
                        errors.push(initialCapitalResult.error);
                    }
                    feeRateResult = parseNumberValue(backtestForm.feeRate, '手续费率', { required: true, min: 0 });
                    if (feeRateResult.error) {
                        errors.push(feeRateResult.error);
                    }
                    fundingRateResult = parseNumberValue(backtestForm.fundingRate, '资金费率', { required: true });
                    if (fundingRateResult.error) {
                        errors.push(fundingRateResult.error);
                    }
                    slippageResult = parseNumberValue(backtestForm.slippageBps, '滑点Bps', { required: true, integer: true, min: 0 });
                    if (slippageResult.error) {
                        errors.push(slippageResult.error);
                    }
                    orderQtyResult = parseNumberValue(backtestForm.orderQty, '单次下单数量', { min: 0.00000001 });
                    if (orderQtyResult.error) {
                        errors.push(orderQtyResult.error);
                    }
                    leverageResult = parseNumberValue(backtestForm.leverage, '杠杆', { integer: true, min: 1 });
                    if (leverageResult.error) {
                        errors.push(leverageResult.error);
                    }
                    takeProfitResult = parseNumberValue(backtestForm.takeProfitPct, '止盈百分比', { min: 0 });
                    if (takeProfitResult.error) {
                        errors.push(takeProfitResult.error);
                    }
                    stopLossResult = parseNumberValue(backtestForm.stopLossPct, '止损百分比', { min: 0 });
                    if (stopLossResult.error) {
                        errors.push(stopLossResult.error);
                    }
                    startTime = null;
                    endTime = null;
                    barCount = null;
                    if (backtestForm.rangeMode === 'time') {
                        startTime = toServerDateTime(backtestForm.startTime);
                        endTime = toServerDateTime(backtestForm.endTime);
                        if (!startTime || !endTime) {
                            errors.push('时间范围需同时填写开始与结束时间');
                        }
                    }
                    else {
                        barCountResult = parseNumberValue(backtestForm.barCount, '回测根数', { required: true, integer: true, min: 1 });
                        if (barCountResult.error) {
                            errors.push(barCountResult.error);
                        }
                        else if (barCountResult.value !== null) {
                            barCount = barCountResult.value;
                        }
                    }
                    if (errors.length > 0) {
                        message = errors[0];
                        setBacktestError(message);
                        error(message);
                        return [2 /*return*/];
                    }
                    payload = {
                        usId: strategy.usId,
                        initialCapital: (_a = initialCapitalResult.value) !== null && _a !== void 0 ? _a : 0,
                        feeRate: (_b = feeRateResult.value) !== null && _b !== void 0 ? _b : 0,
                        fundingRate: (_c = fundingRateResult.value) !== null && _c !== void 0 ? _c : 0,
                        slippageBps: slippageResult.value ? Math.trunc(slippageResult.value) : 0,
                        autoReverse: backtestForm.autoReverse,
                        useStrategyRuntime: backtestForm.useStrategyRuntime,
                        output: {
                            includeTrades: backtestForm.outputTrades,
                            includeEquityCurve: backtestForm.outputEquity,
                            includeEvents: backtestForm.outputEvents,
                            equityCurveGranularity: backtestForm.outputEquityGranularity,
                        },
                    };
                    if (exchange) {
                        payload.exchange = exchange;
                    }
                    if (timeframe) {
                        payload.timeframe = timeframe;
                    }
                    if (symbols.length > 0) {
                        payload.symbols = symbols;
                    }
                    if (backtestForm.rangeMode === 'time') {
                        payload.startTime = startTime !== null && startTime !== void 0 ? startTime : undefined;
                        payload.endTime = endTime !== null && endTime !== void 0 ? endTime : undefined;
                    }
                    else if (barCount !== null) {
                        payload.barCount = barCount;
                    }
                    if (orderQtyResult.value !== null) {
                        payload.orderQtyOverride = orderQtyResult.value;
                    }
                    if (leverageResult.value !== null) {
                        payload.leverageOverride = Math.trunc(leverageResult.value);
                    }
                    if (takeProfitResult.value !== null) {
                        payload.takeProfitPctOverride = takeProfitResult.value;
                    }
                    if (stopLossResult.value !== null) {
                        payload.stopLossPctOverride = stopLossResult.value;
                    }
                    reqId = (0, requestId_1.generateReqId)();
                    ws = (0, network_1.getWsClient)();
                    unsubscribeProgress = ws.on('backtest.progress', function (message) {
                        var _a;
                        if (message.reqId !== reqId) {
                            return;
                        }
                        var payload = ((_a = message.data) !== null && _a !== void 0 ? _a : null);
                        if (!payload) {
                            return;
                        }
                        if (payload.stage) {
                            setBacktestProgressStageCode(payload.stage);
                        }
                        if (payload.stageName) {
                            setBacktestProgressStage(payload.stageName);
                        }
                        if (payload.message) {
                            setBacktestProgressMessage(payload.message);
                        }
                        if (typeof payload.progress === 'number') {
                            setBacktestProgressPercent(payload.progress);
                        }
                        if (typeof payload.foundPositions === 'number') {
                            setBacktestFoundPositions(payload.foundPositions);
                        }
                        if (typeof payload.totalPositions === 'number') {
                            setBacktestTotalPositions(payload.totalPositions);
                        }
                        if (typeof payload.winCount === 'number') {
                            setBacktestWinCount(payload.winCount);
                        }
                        if (typeof payload.lossCount === 'number') {
                            setBacktestLossCount(payload.lossCount);
                        }
                        if (typeof payload.winRate === 'number') {
                            setBacktestWinRate(payload.winRate);
                        }
                        if (Array.isArray(payload.positions) && payload.positions.length > 0) {
                            setBacktestStreamingTrades(function (prev) {
                                var _a, _b;
                                var previewLimit = 100;
                                if (payload.replacePositions) {
                                    return ((_a = payload.positions) !== null && _a !== void 0 ? _a : []).slice(0, previewLimit);
                                }
                                var merged = prev.concat((_b = payload.positions) !== null && _b !== void 0 ? _b : []);
                                if (merged.length <= previewLimit) {
                                    return merged;
                                }
                                return merged.slice(merged.length - previewLimit);
                            });
                        }
                    });
                    setIsBacktestRunning(true);
                    setBacktestError(null);
                    setBacktestProgressStageCode('');
                    setBacktestProgressStage('准备中');
                    setBacktestProgressMessage('等待回测任务启动');
                    setBacktestProgressPercent(0);
                    setBacktestFoundPositions(0);
                    setBacktestTotalPositions(0);
                    setBacktestWinCount(0);
                    setBacktestLossCount(0);
                    setBacktestWinRate(null);
                    setBacktestStreamingTrades([]);
                    _d.label = 1;
                case 1:
                    _d.trys.push([1, 3, 4, 5]);
                    return [4 /*yield*/, onRunBacktest(payload, reqId)];
                case 2:
                    result = _d.sent();
                    setBacktestResult(result);
                    success('回测完成');
                    return [3 /*break*/, 5];
                case 3:
                    err_12 = _d.sent();
                    message = err_12 instanceof Error ? err_12.message : '回测失败';
                    setBacktestError(message);
                    error(message);
                    return [3 /*break*/, 5];
                case 4:
                    unsubscribeProgress();
                    setIsBacktestRunning(false);
                    return [7 /*endfinally*/];
                case 5: return [2 /*return*/];
            }
        });
    }); };
    var formatDurationMs = function (ms) {
        if (ms === null || ms === undefined || Number.isNaN(ms)) {
            return '-';
        }
        return formatDuration(ms);
    };
    var backtestProgressCountLabel = (0, react_1.useMemo)(function () {
        if (backtestProgressStageCode === 'batch_open_phase') {
            return '已检测开仓';
        }
        if (backtestProgressStageCode === 'batch_close_phase') {
            return '已检测平仓';
        }
        if (backtestProgressStageCode === 'collect_positions') {
            return '已汇总仓位';
        }
        return '已处理数量';
    }, [backtestProgressStageCode]);
    var backtestProgressCountValue = backtestTotalPositions > 0
        ? "".concat(backtestFoundPositions, " / ").concat(backtestTotalPositions)
        : "".concat(backtestFoundPositions);
    var renderStats = function (stats) {
        var _a, _b;
        return (<>
      <div className="backtest-stats-grid">
        <div className="backtest-stat">
          <span className="backtest-stat-label">总收益</span>
          <span className="backtest-stat-value">{formatNumber(stats.totalProfit)}</span>
        </div>
        <div className="backtest-stat">
          <span className="backtest-stat-label">总收益率</span>
          <span className="backtest-stat-value">{formatPercent(stats.totalReturn)}</span>
        </div>
        <div className="backtest-stat">
          <span className="backtest-stat-label">最大回撤</span>
          <span className="backtest-stat-value">{formatPercent(stats.maxDrawdown)}</span>
        </div>
        <div className="backtest-stat">
          <span className="backtest-stat-label">胜率</span>
          <span className="backtest-stat-value">{formatPercent(stats.winRate)}</span>
        </div>
        <div className="backtest-stat">
          <span className="backtest-stat-label">交易次数</span>
          <span className="backtest-stat-value">{stats.tradeCount}</span>
        </div>
        <div className="backtest-stat">
          <span className="backtest-stat-label">平均收益</span>
          <span className="backtest-stat-value">{formatNumber(stats.avgProfit)}</span>
        </div>
        <div className="backtest-stat">
          <span className="backtest-stat-label">盈亏比</span>
          <span className="backtest-stat-value">{formatNumber(stats.profitFactor)}</span>
        </div>
        <div className="backtest-stat">
          <span className="backtest-stat-label">平均盈利/亏损</span>
          <span className="backtest-stat-value">
            {formatNumber(stats.avgWin)} / {formatNumber(stats.avgLoss)}
          </span>
        </div>
      </div>
      <div className="backtest-section backtest-section--advanced">
        <div className="backtest-section-title">高级指标</div>
        <div className="backtest-stats-grid">
          <div className="backtest-stat">
            <span className="backtest-stat-label">夏普比率</span>
            <span className="backtest-stat-value">{formatNumber(stats.sharpeRatio)}</span>
          </div>
          <div className="backtest-stat">
            <span className="backtest-stat-label">Sortino 比率</span>
            <span className="backtest-stat-value">{formatNumber(stats.sortinoRatio)}</span>
          </div>
          <div className="backtest-stat">
            <span className="backtest-stat-label">年化收益率</span>
            <span className="backtest-stat-value">{formatPercent(stats.annualizedReturn)}</span>
          </div>
          <div className="backtest-stat">
            <span className="backtest-stat-label">Calmar 比率</span>
            <span className="backtest-stat-value">{formatNumber(stats.calmarRatio)}</span>
          </div>
          <div className="backtest-stat">
            <span className="backtest-stat-label">最大连续亏损次数</span>
            <span className="backtest-stat-value">{(_a = stats.maxConsecutiveLosses) !== null && _a !== void 0 ? _a : '-'}</span>
          </div>
          <div className="backtest-stat">
            <span className="backtest-stat-label">最大连续盈利次数</span>
            <span className="backtest-stat-value">{(_b = stats.maxConsecutiveWins) !== null && _b !== void 0 ? _b : '-'}</span>
          </div>
          <div className="backtest-stat">
            <span className="backtest-stat-label">平均持仓时间</span>
            <span className="backtest-stat-value">{formatDurationMs(stats.avgHoldingMs)}</span>
          </div>
          <div className="backtest-stat">
            <span className="backtest-stat-label">最大回撤持续时间</span>
            <span className="backtest-stat-value">{formatDurationMs(stats.maxDrawdownDurationMs)}</span>
          </div>
        </div>
      </div>
    </>);
    };
    var renderTradeSummary = function (summary) {
        if (!summary) {
            return null;
        }
        return (<div className="backtest-summary-row">
        <span>总数：{summary.totalCount}</span>
        <span>
          胜/负：{summary.winCount}/{summary.lossCount}
        </span>
        <span>最大盈利：{formatNumber(summary.maxProfit)}</span>
        <span>最大亏损：{formatNumber(summary.maxLoss)}</span>
        <span>手续费：{formatNumber(summary.totalFee)}</span>
      </div>);
    };
    var renderEquitySummary = function (summary) {
        if (!summary) {
            return null;
        }
        return (<div className="backtest-summary-row">
        <span>点数：{summary.pointCount}</span>
        <span>
          最大盈利：{formatNumber(summary.maxPeriodProfit)} @ {formatTimestamp(summary.maxPeriodProfitAt)}
        </span>
        <span>
          最大亏损：{formatNumber(summary.maxPeriodLoss)} @ {formatTimestamp(summary.maxPeriodLossAt)}
        </span>
        <span>
          最高权益：{formatNumber(summary.maxEquity)} @ {formatTimestamp(summary.maxEquityAt)}
        </span>
        <span>
          最低权益：{formatNumber(summary.minEquity)} @ {formatTimestamp(summary.minEquityAt)}
        </span>
      </div>);
    };
    var renderEventSummary = function (summary) {
        var _a;
        if (!summary) {
            return null;
        }
        var topTypes = Object.entries((_a = summary.typeCounts) !== null && _a !== void 0 ? _a : {})
            .sort(function (a, b) { return b[1] - a[1]; })
            .slice(0, 3);
        return (<div className="backtest-summary-row">
        <span>总数：{summary.totalCount}</span>
        <span>
          时间范围：{formatTimestamp(summary.firstTimestamp)} ~ {formatTimestamp(summary.lastTimestamp)}
        </span>
        <span>
          类型分布：{topTypes.length === 0 ? '-' : topTypes.map(function (_a) {
            var key = _a[0], value = _a[1];
            return "".concat(key, ":").concat(value);
        }).join(' / ')}
        </span>
      </div>);
    };
    if (!strategy) {
        return null;
    }
    var officialPublished = Boolean(strategy.officialDefId);
    var templatePublished = Boolean(strategy.templateDefId);
    var marketPublished = Boolean(strategy.marketId);
    var officialVersionNo = (_b = strategy.officialVersionNo) !== null && _b !== void 0 ? _b : 0;
    var templateVersionNo = (_c = strategy.templateVersionNo) !== null && _c !== void 0 ? _c : 0;
    var marketVersionNo = (_d = strategy.marketVersionNo) !== null && _d !== void 0 ? _d : 0;
    var officialOutdated = officialPublished && strategy.versionNo > officialVersionNo;
    var templateOutdated = templatePublished && strategy.versionNo > templateVersionNo;
    var marketOutdated = marketPublished && strategy.versionNo > marketVersionNo;
    return (<div className="strategy-detail-dialog">
      <div className="strategy-detail-header">
        <div className="strategy-detail-title-section">
          <h2 className="strategy-detail-title">
            {strategy.aliasName || strategy.defName}
            {strategy.versionNo && <span className="strategy-detail-version">v{strategy.versionNo}</span>}
          </h2>
          <div className={"strategy-detail-status ".concat(getStatusColor(currentStatus))}>
            <div className="status-dot"></div>
            <span>{getStatusText(currentStatus)}</span>
          </div>
        </div>
        <button className="strategy-detail-close" type="button" onClick={onClose} aria-label="关闭">
          <svg width={20} height={20} viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
            <path d="M18 6L6 18M6 6L18 18" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round"/>
          </svg>
        </button>
      </div>

      <div className="strategy-detail-tabs">
        <button type="button" className={"strategy-detail-tab ".concat(activeTab === 'info' ? 'is-active' : '')} onClick={function () { return handleTabChange('info'); }}>
          鍩烘湰淇℃伅
        </button>
        <button type="button" className={"strategy-detail-tab ".concat(activeTab === 'share' ? 'is-active' : '')} onClick={function () { return handleTabChange('share'); }}>
          分享码
        </button>
        <button type="button" className={"strategy-detail-tab ".concat(activeTab === 'history' ? 'is-active' : '')} onClick={function () { return handleTabChange('history'); }}>
          历史版本
        </button>
        <button type="button" className={"strategy-detail-tab ".concat(activeTab === 'positions' ? 'is-active' : '')} onClick={function () { return handleTabChange('positions'); }}>
          仓位
        </button>
        <button type="button" className={"strategy-detail-tab ".concat(activeTab === 'backtest' ? 'is-active' : '')} onClick={function () { return handleTabChange('backtest'); }}>
          回测
        </button>
      </div>

      <div className="strategy-detail-content">
        {activeTab === 'info' && (<div className="strategy-detail-info">
            <div className="strategy-detail-section">
              <h3 className="strategy-detail-section-title">策略状态</h3>
              <div className="strategy-detail-status-controls">
                <button type="button" className={"strategy-status-btn ".concat(currentStatus === 'running' ? 'is-active' : '')} onClick={function () { return handleUpdateStatus('running'); }} disabled={isUpdatingStatus || currentStatus === 'running'}>
                  {isUpdatingStatus && currentStatus !== 'running' ? '更新中...' : '运行中'}
                </button>
                <button type="button" className={"strategy-status-btn ".concat(currentStatus === 'paused' ? 'is-active' : '')} onClick={function () { return handleUpdateStatus('paused'); }} disabled={isUpdatingStatus || currentStatus === 'paused'}>
                  {isUpdatingStatus && currentStatus !== 'paused' ? '更新中...' : '已暂停'}
                </button>
                <button type="button" className={"strategy-status-btn ".concat(currentStatus === 'paused_open_position' ? 'is-active' : '')} onClick={function () { return handleUpdateStatus('paused_open_position'); }} disabled={isUpdatingStatus || currentStatus === 'paused_open_position'}>
                  {isUpdatingStatus && currentStatus !== 'paused_open_position' ? '更新中...' : '暂停开新仓'}
                </button>
                <button type="button" className={"strategy-status-btn ".concat(currentStatus === 'completed' ? 'is-active' : '')} onClick={function () { return handleUpdateStatus('completed'); }} disabled={isUpdatingStatus || currentStatus === 'completed'}>
                  {isUpdatingStatus && currentStatus !== 'completed' ? '更新中...' : '完成'}
                </button>
              </div>
            </div>

            <div className="strategy-detail-section">
              <h3 className="strategy-detail-section-title">策略信息</h3>
              <div className="strategy-detail-info-grid">
                <div className="strategy-detail-info-item">
                  <span className="strategy-detail-info-label">策略名称</span>
                  <span className="strategy-detail-info-value">{strategy.aliasName || strategy.defName}</span>
                </div>
                <div className="strategy-detail-info-item">
                  <span className="strategy-detail-info-label">版本号</span>
                  <span className="strategy-detail-info-value">v{strategy.versionNo}</span>
                </div>
                {strategy.description && (<div className="strategy-detail-info-item strategy-detail-info-item--full">
                    <span className="strategy-detail-info-label">描述</span>
                    <span className="strategy-detail-info-value">{strategy.description}</span>
                  </div>)}
              </div>
            </div>

            <div className="strategy-detail-section">
              <h3 className="strategy-detail-section-title">操作</h3>
              <div className="strategy-detail-actions">
                <button type="button" className="strategy-detail-action-btn strategy-detail-action-btn--primary" onClick={handleCreateVersion}>
                  创建新版本
                </button>
                {!marketPublished && (<button type="button" className="strategy-detail-action-btn" onClick={function () { return setIsMarketConfirmOpen(true); }}>
                    公开到策略广场
                  </button>)}
                {marketPublished && marketOutdated && (<button type="button" className="strategy-detail-action-btn" onClick={function () { return setSyncTarget('market'); }}>
                    发布最新版本到广场
                  </button>)}
                {canPublish && (<>
                    {!officialPublished && (<button type="button" className="strategy-detail-action-btn" onClick={function () { return setPublishTarget('official'); }}>
                        发布到官方
                      </button>)}
                    {officialPublished && officialOutdated && (<button type="button" className="strategy-detail-action-btn" onClick={function () { return setSyncTarget('official'); }}>
                        发布最新版本到官方
                      </button>)}
                    {officialPublished && (<button type="button" className="strategy-detail-action-btn strategy-detail-action-btn--danger" onClick={function () { return setRemoveTarget('official'); }}>
                        从官方策略中移除
                      </button>)}
                    {!templatePublished && (<button type="button" className="strategy-detail-action-btn" onClick={function () { return setPublishTarget('template'); }}>
                        发布到模板
                      </button>)}
                    {templatePublished && templateOutdated && (<button type="button" className="strategy-detail-action-btn" onClick={function () { return setSyncTarget('template'); }}>
                        发布最新版本到模板
                      </button>)}
                    {templatePublished && (<button type="button" className="strategy-detail-action-btn strategy-detail-action-btn--danger" onClick={function () { return setRemoveTarget('template'); }}>
                        从策略模板中移除
                      </button>)}
                  </>)}
                <button type="button" className="strategy-detail-action-btn strategy-detail-action-btn--danger" onClick={function () { return onDelete(strategy.usId); }}>
                  删除策略
                </button>
              </div>
              <div className="strategy-detail-actions strategy-detail-actions--secondary">
                <button type="button" className="strategy-detail-action-btn" onClick={handleEditStrategy} disabled={isCheckingPositions}>
                  {isCheckingPositions ? '检查仓位中...' : '修改策略'}
                </button>
              </div>
            </div>
          </div>)}

        {activeTab === 'share' && (<div className="strategy-detail-share">
            <div className="strategy-detail-share-wrapper">
              <StrategyShareDialog_1.default strategyName={strategy.aliasName || strategy.defName} onCreateShare={handleCreateShare} onClose={function () { }}/>
            </div>
          </div>)}

        {activeTab === 'history' && (<div className="strategy-detail-history">
            <StrategyHistoryDialog_1.default versions={historyVersions} selectedVersionId={selectedHistoryVersionId} onSelectVersion={setSelectedHistoryVersionId} onClose={function () { }} isLoading={isHistoryLoading}/>
          </div>)}

        {activeTab === 'positions' && (<div className="strategy-detail-positions">
            <div className="strategy-detail-positions-card">
              <div className="strategy-detail-positions-header">
                <div>
                  <div className="strategy-detail-positions-title">仓位历史</div>
                  <div className="strategy-detail-positions-hint">
                    协议请求：POST /api/positions/by-strategy（type=position.list.by_strategy）
                  </div>
                </div>
                <div className="strategy-detail-positions-actions">
                  <button type="button" className="strategy-detail-positions-action strategy-detail-positions-action--danger" onClick={function () { return setIsClosePositionsConfirmOpen(true); }} disabled={isPositionsLoading || isClosingPositions}>
                    一键平仓
                  </button>
                  <button type="button" className="strategy-detail-positions-action" onClick={function () { return handleLoadPositions(true); }} disabled={isPositionsLoading}>
                    {isPositionsLoading ? '加载中...' : '刷新'}
                  </button>
                </div>
              </div>
              {isPositionsLoading ? (<div className="strategy-detail-empty">加载中...</div>) : positions.length === 0 ? (<div className="strategy-detail-empty">暂无仓位记录</div>) : (<div className="strategy-detail-positions-table">
                  <div className="positions-table-header">
                    <div className="positions-table-cell">仓位ID</div>
                    <div className="positions-table-cell">浜ゆ槗鎵€</div>
                    <div className="positions-table-cell">浜ゆ槗瀵</div>
                    <div className="positions-table-cell">方向</div>
                    <div className="positions-table-cell">鐘舵€</div>
                    <div className="positions-table-cell">开仓价</div>
                    <div className="positions-table-cell">数量</div>
                    <div className="positions-table-cell">止损价</div>
                    <div className="positions-table-cell">止盈价</div>
                    <div className="positions-table-cell">启用移动止盈</div>
                    <div className="positions-table-cell">已触发</div>
                    <div className="positions-table-cell">绉诲姩止损价</div>
                    <div className="positions-table-cell">骞充粨原因</div>
                    <div className="positions-table-cell">开仓时间</div>
                    <div className="positions-table-cell">平仓时间</div>
                    <div className="positions-table-cell">操作</div>
                  </div>
                  <div className="positions-table-body">
                    {positions.map(function (position, index) {
                    var _a, _b, _c, _d, _e, _f;
                    var isOpenPosition = ((_a = position.status) === null || _a === void 0 ? void 0 : _a.toLowerCase()) === 'open';
                    return (<div className="positions-table-row" key={(_b = position.positionId) !== null && _b !== void 0 ? _b : "".concat((_c = position.openedAt) !== null && _c !== void 0 ? _c : 'pos', "-").concat(index)}>
                          <div className="positions-table-cell">{(_d = position.positionId) !== null && _d !== void 0 ? _d : '-'}</div>
                          <div className="positions-table-cell">{(_e = position.exchange) !== null && _e !== void 0 ? _e : '-'}</div>
                          <div className="positions-table-cell">{(_f = position.symbol) !== null && _f !== void 0 ? _f : '-'}</div>
                          <div className="positions-table-cell">{formatSide(position.side)}</div>
                          <div className="positions-table-cell">{formatStatus(position.status, position.closeReason)}</div>
                          <div className="positions-table-cell">{formatNumber(position.entryPrice)}</div>
                          <div className="positions-table-cell">{formatNumber(position.qty)}</div>
                          <div className="positions-table-cell">{formatNumber(position.stopLossPrice)}</div>
                          <div className="positions-table-cell">{formatNumber(position.takeProfitPrice)}</div>
                          <div className="positions-table-cell">{formatBoolean(position.trailingEnabled)}</div>
                          <div className="positions-table-cell">{formatBoolean(position.trailingTriggered)}</div>
                          <div className="positions-table-cell">{formatNumber(position.trailingStopPrice)}</div>
                          <div className="positions-table-cell">{formatCloseReason(position.closeReason)}</div>
                          <div className="positions-table-cell">{formatDateTimeLocal(position.openedAt)}</div>
                          <div className="positions-table-cell">{formatDateTimeLocal(position.closedAt)}</div>
                          <div className="positions-table-cell positions-table-cell--actions">
                            {isOpenPosition ? (<button type="button" className="positions-table-action positions-table-action--danger" onClick={function () { return setClosePositionTarget(position); }} disabled={isClosingPosition || isClosingPositions}>
                                平掉此仓
                              </button>) : (<span className="positions-table-action positions-table-action--muted">-</span>)}
                          </div>
                        </div>);
                })}
                  </div>
                </div>)}
            </div>
          </div>)}

        {activeTab === 'backtest' && (<div className="strategy-detail-backtest">
            <div className="backtest-layout">
              <div className="backtest-card">
                <div className="backtest-card-title">回测参数</div>
                <div className="backtest-form">
                  <div className="backtest-section">
                    <div className="backtest-section-title">鍩虹淇℃伅</div>
                    <div className="backtest-form-grid">
                      <label className="backtest-field">
                        <span className="backtest-label">交易所</span>
                        <select className="backtest-select" value={backtestForm.exchange} onChange={function (event) { return updateBacktestField('exchange', event.target.value); }}>
                          <option value="">请选择</option>
                          {availableExchanges.map(function (exchange) { return (<option key={exchange} value={exchange}>
                              {exchange}
                            </option>); })}
                        </select>
                      </label>
                      <label className="backtest-field">
                        <span className="backtest-label">周期</span>
                        <select className="backtest-select" value={backtestForm.timeframe} onChange={function (event) { return updateBacktestField('timeframe', event.target.value); }} disabled={!backtestForm.exchange || availableTimeframes.length === 0}>
                          <option value="">请选择</option>
                          {availableTimeframes.map(function (timeframe) { return (<option key={timeframe} value={timeframe}>
                              {timeframe}
                            </option>); })}
                        </select>
                      </label>
                      <label className="backtest-field backtest-field--full">
                        <span className="backtest-label">标的列表</span>
                        <div className="backtest-symbols-input-wrapper">
                          <input className="backtest-input" placeholder="BTC/USDT, ETH/USDT" value={backtestForm.symbols} onChange={function (event) { return updateBacktestField('symbols', event.target.value); }} disabled={!backtestForm.exchange}/>
                          {backtestForm.exchange && availableSymbols.length > 0 && (<div className="backtest-symbols-suggestions">
                              {availableSymbols.map(function (symbol) {
                    var symbols = backtestForm.symbols.split(/[,\s]+/).filter(Boolean);
                    var isSelected = symbols.includes(symbol);
                    return (<button key={symbol} type="button" className={"backtest-symbol-tag ".concat(isSelected ? 'selected' : '')} onClick={function () {
                            var symbols = backtestForm.symbols.split(/[,\s]+/).filter(Boolean);
                            if (isSelected) {
                                var newSymbols = symbols.filter(function (s) { return s !== symbol; });
                                updateBacktestField('symbols', newSymbols.join(', '));
                            }
                            else {
                                symbols.push(symbol);
                                updateBacktestField('symbols', symbols.join(', '));
                            }
                        }}>
                                    {symbol}
                                  </button>);
                })}
                            </div>)}
                        </div>
                        <span className="backtest-hint">多个标的用逗号或空格分隔，或点击下方标签选择</span>
                      </label>
                    </div>
                  </div>

                  <div className="backtest-section">
                    <div className="backtest-section-title">时间范围</div>
                    {supportedTimeRange && (<div className="backtest-time-range-info">
                        <span className="backtest-time-range-label">支持的回测时间范围：</span>
                        <span className="backtest-time-range-value">
                          {supportedTimeRange.start.toLocaleString('zh-CN', {
                    year: 'numeric',
                    month: '2-digit',
                    day: '2-digit',
                    hour: '2-digit',
                    minute: '2-digit',
                })}{' '}
                          ~{' '}
                          {supportedTimeRange.end.toLocaleString('zh-CN', {
                    year: 'numeric',
                    month: '2-digit',
                    day: '2-digit',
                    hour: '2-digit',
                    minute: '2-digit',
                })}
                        </span>
                        <button type="button" className="backtest-full-range-btn" onClick={handleFullRangeBacktest}>
                          全量回测
                        </button>
                      </div>)}
                    <div className="backtest-form-grid">
                      <label className="backtest-field">
                        <span className="backtest-label">回测方式</span>
                        <select className="backtest-select" value={backtestForm.rangeMode} onChange={function (event) {
                return updateBacktestField('rangeMode', event.target.value);
            }}>
                          <option value="bars">按根数</option>
                          <option value="time">按时间范围</option>
                        </select>
                      </label>
                      {backtestForm.rangeMode === 'bars' ? (<label className="backtest-field">
                          <span className="backtest-label">回测根数</span>
                          <input className="backtest-input" type="number" min={1} value={backtestForm.barCount} onChange={function (event) { return updateBacktestField('barCount', event.target.value); }}/>
                        </label>) : (<>
                          <label className="backtest-field">
                            <span className="backtest-label">开始时间</span>
                            <input className="backtest-input" type="datetime-local" value={backtestForm.startTime} onChange={function (event) { return updateBacktestField('startTime', event.target.value); }}/>
                          </label>
                          <label className="backtest-field">
                            <span className="backtest-label">结束时间</span>
                            <input className="backtest-input" type="datetime-local" value={backtestForm.endTime} onChange={function (event) { return updateBacktestField('endTime', event.target.value); }}/>
                          </label>
                        </>)}
                    </div>
                    <div className="backtest-hint">按时间范围时使用本地时间格式，需同时填写开始与结束</div>
                  </div>

                  <div className="backtest-section">
                    <div className="backtest-section-title">资金与交易</div>
                    <div className="backtest-form-grid">
                      <label className="backtest-field">
                        <span className="backtest-label">初始资金</span>
                        <input className="backtest-input" type="number" min={0} step="0.01" value={backtestForm.initialCapital} onChange={function (event) { return updateBacktestField('initialCapital', event.target.value); }}/>
                      </label>
                      <label className="backtest-field">
                        <span className="backtest-label">单次下单数量</span>
                        <input className="backtest-input" type="number" min={0} step="0.0001" value={backtestForm.orderQty} onChange={function (event) { return updateBacktestField('orderQty', event.target.value); }}/>
                      </label>
                      <label className="backtest-field">
                        <span className="backtest-label">杠杆</span>
                        <input className="backtest-input" type="number" min={1} step="1" value={backtestForm.leverage} onChange={function (event) { return updateBacktestField('leverage', event.target.value); }}/>
                      </label>
                    </div>
                  </div>

                  <div className="backtest-section">
                    <div className="backtest-section-title">止盈止损</div>
                    <div className="backtest-form-grid">
                      <label className="backtest-field">
                        <span className="backtest-label">止盈百分比</span>
                        <input className="backtest-input" type="number" min={0} step="0.0001" value={backtestForm.takeProfitPct} onChange={function (event) { return updateBacktestField('takeProfitPct', event.target.value); }}/>
                      </label>
                      <label className="backtest-field">
                        <span className="backtest-label">止损百分比</span>
                        <input className="backtest-input" type="number" min={0} step="0.0001" value={backtestForm.stopLossPct} onChange={function (event) { return updateBacktestField('stopLossPct', event.target.value); }}/>
                      </label>
                    </div>
                    <div className="backtest-hint">小数形式，例如 0.02 表示 2%</div>
                  </div>

                  <div className="backtest-section">
                    <div className="backtest-section-title">费用与滑点</div>
                    <div className="backtest-form-grid">
                      <label className="backtest-field">
                        <span className="backtest-label">手续费率</span>
                        <input className="backtest-input" type="number" min={0} step="0.0001" value={backtestForm.feeRate} onChange={function (event) { return updateBacktestField('feeRate', event.target.value); }}/>
                      </label>
                      <label className="backtest-field">
                        <span className="backtest-label">资金费率</span>
                        <input className="backtest-input" type="number" step="0.0001" value={backtestForm.fundingRate} onChange={function (event) { return updateBacktestField('fundingRate', event.target.value); }}/>
                      </label>
                      <label className="backtest-field">
                        <span className="backtest-label">滑点Bps</span>
                        <input className="backtest-input" type="number" min={0} step="1" value={backtestForm.slippageBps} onChange={function (event) { return updateBacktestField('slippageBps', event.target.value); }}/>
                      </label>
                    </div>
                    <div className="backtest-hint">手续费率默认 0.0004（0.04%）</div>
                  </div>

                  <div className="backtest-section">
                    <div className="backtest-section-title">运行控制</div>
                    <div className="backtest-toggle-row">
                      <label className="backtest-toggle">
                        <input type="checkbox" checked={backtestForm.autoReverse} onChange={function (event) { return updateBacktestField('autoReverse', event.target.checked); }}/>
                        <span>自动反向</span>
                      </label>
                      <label className="backtest-toggle">
                        <input type="checkbox" checked={backtestForm.useStrategyRuntime} onChange={function (event) { return updateBacktestField('useStrategyRuntime', event.target.checked); }}/>
                        <span>启用策略运行时间</span>
                      </label>
                    </div>
                  </div>

                  <div className="backtest-section">
                    <div className="backtest-section-title">输出选项</div>
                    <div className="backtest-toggle-row">
                      <label className="backtest-toggle">
                        <input type="checkbox" checked={backtestForm.outputTrades} onChange={function (event) { return updateBacktestField('outputTrades', event.target.checked); }}/>
                        <span>交易明细</span>
                      </label>
                      <label className="backtest-toggle">
                        <input type="checkbox" checked={backtestForm.outputEquity} onChange={function (event) { return updateBacktestField('outputEquity', event.target.checked); }}/>
                        <span>资金曲线</span>
                      </label>
                      <label className="backtest-toggle">
                        <input type="checkbox" checked={backtestForm.outputEvents} onChange={function (event) { return updateBacktestField('outputEvents', event.target.checked); }}/>
                        <span>事件日志</span>
                      </label>
                    </div>
                    <div className="backtest-form-grid">
                      <label className="backtest-field">
                        <span className="backtest-label">资金曲线周期</span>
                        <select className="backtest-select" value={backtestForm.outputEquityGranularity} onChange={function (event) { return updateBacktestField('outputEquityGranularity', event.target.value); }} disabled={!backtestForm.outputEquity}>
                          {equityGranularityOptions.map(function (item) { return (<option key={item.value} value={item.value}>
                              {item.label}
                            </option>); })}
                        </select>
                      </label>
                    </div>
                    <div className="backtest-hint">资金曲线按周期聚合输出，显著降低结果体积与内存占用</div>
                  </div>

                  {backtestError && <div className="backtest-error">{backtestError}</div>}

                  <div className="backtest-actions">
                    <button type="button" className="backtest-btn ghost" onClick={handleResetBacktest}>
                      重置
                    </button>
                    <button type="button" className="backtest-btn primary" onClick={handleRunBacktest} disabled={isBacktestRunning}>
                      {isBacktestRunning ? '回测中...' : '开始回测'}
                    </button>
                  </div>
                </div>
              </div>

              <div className="backtest-card">
                <div className="backtest-card-title">回测结果</div>
                {isBacktestRunning ? (<div className="backtest-result">
                    <div className="backtest-empty">回测运行中...</div>
                    <div className="backtest-result-meta">
                      <span>当前阶段：{backtestProgressStage || '-'}</span>
                      <span>阶段说明：{backtestProgressMessage || '-'}</span>
                      <span>阶段进度：{backtestProgressPercent === null ? '-' : formatPercent(backtestProgressPercent)}</span>
                      <span>{backtestProgressCountLabel}：{backtestProgressCountValue}</span>
                      {backtestProgressStageCode === 'batch_close_phase' && (<span>当前胜率：{backtestWinRate === null ? '-' : formatPercent(backtestWinRate)}</span>)}
                      {backtestProgressStageCode === 'batch_close_phase' && (<span>胜/负：{backtestWinCount}/{backtestLossCount}</span>)}
                    </div>
                    {backtestStreamingTrades.length > 0 && (<details className="backtest-details" open>
                        <summary>最近仓位预览（{backtestStreamingTrades.length}）</summary>
                        <div className="backtest-table-wrapper">
                          <table className="backtest-table">
                            <thead>
                              <tr>
                                <th>标的</th>
                                <th>方向</th>
                                <th>开仓时间</th>
                                <th>平仓时间</th>
                                <th>开仓价</th>
                                <th>平仓价</th>
                                <th>数量</th>
                                <th>盈亏</th>
                              </tr>
                            </thead>
                            <tbody>
                              {backtestStreamingTrades.map(function (trade, tradeIndex) { return (<tr key={"".concat(trade.entryTime, "-").concat(trade.exitTime, "-").concat(tradeIndex)}>
                                  <td>{trade.symbol || '-'}</td>
                                  <td>{trade.side}</td>
                                  <td>{formatTimestamp(trade.entryTime)}</td>
                                  <td>{formatTimestamp(trade.exitTime)}</td>
                                  <td>{formatNumber(trade.entryPrice)}</td>
                                  <td>{formatNumber(trade.exitPrice)}</td>
                                  <td>{formatNumber(trade.qty)}</td>
                                  <td>{formatNumber(trade.pnL)}</td>
                                </tr>); })}
                            </tbody>
                          </table>
                        </div>
                        <div className="backtest-hint">仅展示最近 100 条仓位预览，完整结果将在回测结束后返回。</div>
                      </details>)}
                  </div>) : backtestResult ? (<div className="backtest-result">
                    <div className="backtest-result-meta">
                      <span>交易所：{backtestResult.exchange || '-'}</span>
                      <span>周期：{backtestResult.timeframe || '-'}</span>
                      <span>资金曲线周期：{backtestResult.equityCurveGranularity || '-'}</span>
                      <span>
                        时间：{formatTimestamp(backtestResult.startTimestamp)} ~ {formatTimestamp(backtestResult.endTimestamp)}
                      </span>
                      <span>总Bar：{backtestResult.totalBars}</span>
                      <span>耗时：{formatDuration(backtestResult.durationMs)}</span>
                    </div>

                    <div className="backtest-section">
                      <div className="backtest-section-title">汇总统计</div>
                      {renderStats(backtestResult.totalStats)}
                    </div>

                    <div className="backtest-section">
                      <div className="backtest-section-title">按标的统计</div>
                      <div className="backtest-symbols">
                        {backtestResult.symbols.length === 0 ? (<div className="backtest-empty">暂无标的结果</div>) : (backtestResult.symbols.map(function (symbolResult) {
                    var _a, _b, _c, _d, _e, _f, _g, _h, _j, _k, _l, _m;
                    var tradeCount = (_d = (_b = (_a = symbolResult.tradeSummary) === null || _a === void 0 ? void 0 : _a.totalCount) !== null && _b !== void 0 ? _b : (_c = symbolResult.tradesRaw) === null || _c === void 0 ? void 0 : _c.length) !== null && _d !== void 0 ? _d : 0;
                    var equityCount = (_h = (_f = (_e = symbolResult.equitySummary) === null || _e === void 0 ? void 0 : _e.pointCount) !== null && _f !== void 0 ? _f : (_g = symbolResult.equityCurveRaw) === null || _g === void 0 ? void 0 : _g.length) !== null && _h !== void 0 ? _h : 0;
                    var eventCount = (_m = (_k = (_j = symbolResult.eventSummary) === null || _j === void 0 ? void 0 : _j.totalCount) !== null && _k !== void 0 ? _k : (_l = symbolResult.eventsRaw) === null || _l === void 0 ? void 0 : _l.length) !== null && _m !== void 0 ? _m : 0;
                    return (<div className="backtest-symbol-card" key={symbolResult.symbol}>
                                <div className="backtest-symbol-header">
                                  <div className="backtest-symbol-title">{symbolResult.symbol}</div>
                                  <div className="backtest-symbol-meta">
                                    Bars: {symbolResult.bars} | 初始资金: {formatNumber(symbolResult.initialCapital)}
                                  </div>
                                </div>
                                {renderStats(symbolResult.stats)}

                                <details className="backtest-details">
                                  <summary>交易明细（{tradeCount}）</summary>
                                  {renderTradeSummary(symbolResult.tradeSummary)}
                                  <LazyTable rawItems={symbolResult.tradesRaw} parseItem={function (raw) { return parseJsonSafe(raw); }} colSpan={10} emptyText="暂无交易" columns={<tr>
                                        <th>方向</th>
                                        <th>开仓时间</th>
                                        <th>平仓时间</th>
                                        <th>开仓价</th>
                                        <th>平仓价</th>
                                        <th>数量</th>
                                        <th>手续费</th>
                                        <th>盈亏</th>
                                        <th>原因</th>
                                        <th>滑点Bps</th>
                                      </tr>} renderRow={function (trade, tradeIndex) { return (<tr key={"".concat(trade.entryTime, "-").concat(tradeIndex)}>
                                        <td>{trade.side}</td>
                                        <td>{formatTimestamp(trade.entryTime)}</td>
                                        <td>{formatTimestamp(trade.exitTime)}</td>
                                        <td>{formatNumber(trade.entryPrice)}</td>
                                        <td>{formatNumber(trade.exitPrice)}</td>
                                        <td>{formatNumber(trade.qty)}</td>
                                        <td>{formatNumber(trade.fee)}</td>
                                        <td>{formatNumber(trade.pnL)}</td>
                                        <td>{trade.exitReason || '-'}</td>
                                        <td>{trade.slippageBps}</td>
                                      </tr>); }}/>
                                </details>

                                <details className="backtest-details">
                                  <summary>资金曲线（{equityCount}）</summary>
                                  {renderEquitySummary(symbolResult.equitySummary)}
                                  <LazyTable rawItems={symbolResult.equityCurveRaw} parseItem={function (raw) { return parseJsonSafe(raw); }} colSpan={6} emptyText="暂无资金曲线" columns={<tr>
                                        <th>时间</th>
                                        <th>权益</th>
                                        <th>已实现</th>
                                        <th>未实现</th>
                                        <th>区间已实现</th>
                                        <th>区间未实现</th>
                                      </tr>} renderRow={function (point, pointIndex) { return (<tr key={"".concat(point.timestamp, "-").concat(pointIndex)}>
                                        <td>{formatTimestamp(point.timestamp)}</td>
                                        <td>{formatNumber(point.equity)}</td>
                                        <td>{formatNumber(point.realizedPnl)}</td>
                                        <td>{formatNumber(point.unrealizedPnl)}</td>
                                        <td>{formatNumber(point.periodRealizedPnl)}</td>
                                        <td>{formatNumber(point.periodUnrealizedPnl)}</td>
                                      </tr>); }}/>
                                </details>

                                <details className="backtest-details">
                                  <summary>事件日志（{eventCount}）</summary>
                                  {renderEventSummary(symbolResult.eventSummary)}
                                  <LazyTable rawItems={symbolResult.eventsRaw} parseItem={function (raw) { return parseJsonSafe(raw); }} colSpan={3} emptyText="暂无事件" columns={<tr>
                                        <th>时间</th>
                                        <th>类型</th>
                                        <th>内容</th>
                                      </tr>} renderRow={function (evt, evtIndex) { return (<tr key={"".concat(evt.timestamp, "-").concat(evtIndex)}>
                                        <td>{formatTimestamp(evt.timestamp)}</td>
                                        <td>{evt.type}</td>
                                        <td>{evt.message}</td>
                                      </tr>); }}/>
                                </details>
                              </div>);
                }))}
                      </div>
                    </div>
                  </div>) : (<div className="backtest-empty">暂无回测结果</div>)}
              </div>
            </div>
          </div>)}
      </div>
      <AlertDialog_1.default open={publishTarget !== null} title={publishTarget === 'official' ? '发布到官方策略库' : '发布到策略模板库'} description={publishTarget === 'official' ? '确认发布到官方策略库吗？发布后其他用户可使用该策略。' : '确认发布到策略模板库吗？发布后其他用户可使用该模板。'} helperText="发布后无法撤销，请谨慎操作" cancelText="取消" confirmText={isPublishing ? '发布中...' : '确认发布'} onCancel={function () { return setPublishTarget(null); }} onClose={function () { return setPublishTarget(null); }} onConfirm={function () {
            if (publishTarget) {
                handlePublish(publishTarget);
            }
        }}/>
      <AlertDialog_1.default open={syncTarget !== null} title={syncTarget === 'official'
            ? '发布最新版本到官方策略库'
            : syncTarget === 'template'
                ? '发布最新版本到策略模板库'
                : '发布最新版本到策略广场'} description={syncTarget === 'official'
            ? '确认将最新版本同步到官方策略库吗？'
            : syncTarget === 'template'
                ? '确认将最新版本同步到策略模板库吗？'
                : '确认将最新版本同步到策略广场吗？'} helperText="同步后会覆盖公开版本，请谨慎操作" cancelText="取消" confirmText={isSyncing ? '发布中...' : '确认发布'} onCancel={function () { return setSyncTarget(null); }} onClose={function () { return setSyncTarget(null); }} onConfirm={function () {
            if (syncTarget) {
                handleSync(syncTarget);
            }
        }}/>
      <AlertDialog_1.default open={removeTarget !== null} title={removeTarget === 'official' ? '从官方策略中移除' : '从策略模板中移除'} description={removeTarget === 'official'
            ? '确认将该策略从官方策略库移除吗？'
            : '确认将该策略从策略模板库移除吗？'} helperText="移除后其他用户将无法继续使用该发布记录。" cancelText="取消" confirmText={isRemoving ? '移除中...' : '确认移除'} danger={true} onCancel={function () { return setRemoveTarget(null); }} onClose={function () { return setRemoveTarget(null); }} onConfirm={function () {
            if (removeTarget) {
                handleRemove(removeTarget);
            }
        }}/>
      <AlertDialog_1.default open={isMarketConfirmOpen} title="公开到策略广场" description="确认将该策略公开到策略广场吗？公开后所有用户都可查看。" helperText="公开后可继续更新版本。" cancelText="取消" confirmText={isMarketPublishing ? '公开中...' : '确认公开'} onCancel={function () { return setIsMarketConfirmOpen(false); }} onClose={function () { return setIsMarketConfirmOpen(false); }} onConfirm={handlePublishMarket}/>
      <AlertDialog_1.default open={isEditConfirmOpen} title="提示" description={"\u5F53\u524D\u6709 ".concat(openPositionCount, " \u4E2A\u4ED3\u4F4D\u672A\u5E73\u4ED3\uFF0C\u662F\u5426\u4E00\u952E\u5E73\u4ED3\u540E\u524D\u5F80\u7F16\u8F91\uFF1F")} cancelText="取消" confirmText="前往编辑" onCancel={function () { return setIsEditConfirmOpen(false); }} onClose={function () { return setIsEditConfirmOpen(false); }} onConfirm={function () {
            if (strategy) {
                setIsEditConfirmOpen(false);
                onEditStrategy(strategy.usId);
                onClose();
            }
        }}/>
      <AlertDialog_1.default open={isClosePositionsConfirmOpen} title="一键平仓" description="确认将该策略暂停并平掉所有仓位吗？系统将分多空两次平仓。" helperText="该操作为人工平仓，将记录为手动平仓。" cancelText="取消" confirmText={isClosingPositions ? '处理中...' : '确认平仓'} danger={true} onCancel={function () { return setIsClosePositionsConfirmOpen(false); }} onClose={function () { return setIsClosePositionsConfirmOpen(false); }} onConfirm={handleCloseAllPositions}/>
      <AlertDialog_1.default open={closePositionTarget !== null} title="平掉此仓" description={closePositionTarget
            ? "\u786E\u8BA4\u5E73\u6389\u8BE5\u4ED3\u4F4D\u5417\uFF1F".concat((_e = closePositionTarget.exchange) !== null && _e !== void 0 ? _e : '-', " ").concat((_f = closePositionTarget.symbol) !== null && _f !== void 0 ? _f : '-', " ").concat(formatSide(closePositionTarget.side), " \u6570\u91CF ").concat(formatNumber(closePositionTarget.qty))
            : undefined} helperText="该操作为人工平仓，将记录为手动平仓。" cancelText="取消" confirmText={isClosingPosition ? '处理中...' : '确认平仓'} danger={true} onCancel={function () { return setClosePositionTarget(null); }} onClose={function () { return setClosePositionTarget(null); }} onConfirm={handleClosePosition}/>
    </div>);
};
exports.default = StrategyDetailDialog;
