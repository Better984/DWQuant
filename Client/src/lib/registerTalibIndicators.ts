import {
  IndicatorSeries,
  PolygonType,
  getSupportedIndicators,
  registerIndicator,
  type Indicator,
  type IndicatorFigure,
  type IndicatorFigureStyle,
  type IndicatorFigureStylesCallbackData,
  type IndicatorTemplate,
} from "klinecharts";
import type { KLineData } from "klinecharts";
import { ensureTalibReady } from "./talibInit";
import {
  calcTalibIndicator,
  type TalibCalcResult,
  type TalibRuntimeCalcSpec,
} from "./talibIndicatorAdapter";

export type TalibIndicatorInputOption = {
  label: string;
  value: string;
};

export type TalibIndicatorInputSlot = {
  key: string;
  label: string;
  defaultValue: string;
  options: TalibIndicatorInputOption[];
};

export type TalibIndicatorParamEnumOption = {
  label: string;
  value: number;
  description?: string;
};

export type TalibIndicatorParamDefinition = {
  key: string;
  label: string;
  description?: string;
  type: "number" | "enum";
  valueType: "integer" | "double" | "matype";
  defaultValue: number;
  enumOptions: TalibIndicatorParamEnumOption[];
};

export type TalibIndicatorParamConfig = {
  defaultParams: number[];
  paramLabels: string[];
};

export type TalibIndicatorEditorSchema = {
  name: string;
  code: string;
  talibCode: string;
  group: string;
  pane: "main" | "sub";
  inputSlots: TalibIndicatorInputSlot[];
  paramDefinitions: TalibIndicatorParamDefinition[];
  defaultParams: number[];
  paramLabels: string[];
};

export type TalibRegisteredIndicatorMeta = {
  name: string;
  code: string;
  talibCode: string;
  pane: "main" | "sub";
  group: string;
};

type TalibConfigEnumOption = {
  label?: string;
  value?: number;
  name?: string;
};

type TalibConfigCommonOption = {
  key?: string;
  desc?: string;
  type?: string;
  enum?: TalibConfigEnumOption[];
  default?: number;
};

type TalibConfigOption = {
  key?: string;
  desc?: string;
  $ref?: string;
};

type TalibConfigOutput = {
  key?: string;
  hint?: string;
};

type TalibConfigIndicator = {
  code: string;
  name_en?: string;
  method?: string;
  group?: string;
  indicator_type?: string;
  inputs?: {
    series?: string[];
  };
  options?: TalibConfigOption[];
  outputs?: TalibConfigOutput[];
};

type TalibConfigRoot = {
  common?: Record<string, TalibConfigCommonOption>;
  indicators: TalibConfigIndicator[];
};

type TalibMetaInput = {
  name: string;
};

type TalibMetaOption = {
  name: string;
  displayName?: string;
  defaultValue?: number;
  type?: string;
};

type TalibMetaOutput = {
  name: string;
  plotHint?: string;
};

type TalibMetaIndicator = {
  description?: string;
  group?: string;
  camelCaseName?: string;
  inputs: TalibMetaInput[];
  options: TalibMetaOption[];
  outputs: TalibMetaOutput[];
};

type TalibMetaRoot = Record<string, TalibMetaIndicator>;

type RuntimeIndicatorSpec = {
  name: string;
  backendCode: string;
  talibCode: string;
  group: string;
  series: IndicatorSeries;
  shouldOhlc: boolean;
  shouldFormatBigNumber: boolean;
  minValue: number | null;
  maxValue: number | null;
  defaultParams: number[];
  paramLabels: string[];
  paramDefinitions: TalibIndicatorParamDefinition[];
  inputSlots: TalibIndicatorInputSlot[];
  figures: Array<IndicatorFigure<TalibCalcResult>>;
  calcSpec: TalibRuntimeCalcSpec;
};

const TALIB_INPUT_OPTIONS: TalibIndicatorInputOption[] = [
  { value: "Close", label: "Close" },
  { value: "Open", label: "Open" },
  { value: "High", label: "High" },
  { value: "Low", label: "Low" },
  { value: "Volume", label: "Volume" },
  { value: "HL2", label: "HL2" },
  { value: "HLC3", label: "HLC3" },
  { value: "OHLC4", label: "OHLC4" },
  { value: "OC2", label: "OC2" },
  { value: "HLCC4", label: "HLCC4" },
];

const TALIB_INPUT_CANONICAL_BY_KEY = new Map<string, string>(
  TALIB_INPUT_OPTIONS.map((option) => [option.value.toUpperCase(), option.value])
);

const TALIB_CODE_ALIAS: Record<string, string> = {
  CONCEALINGBABYSWALLOW: "CDLCONCEALBABYSWALL",
  GAPSIDEBYSIDEWHITELINES: "CDLGAPSIDESIDEWHITE",
  HIKKAKEMODIFIED: "CDLHIKKAKEMOD",
  IDENTICALTHREECROWS: "CDLIDENTICAL3CROWS",
  PIERCINGLINE: "CDLPIERCING",
  RISINGFALLINGTHREEMETHODS: "CDLRISEFALL3METHODS",
  TAKURILINE: "CDLTAKURI",
  UNIQUETHREERIVER: "CDLUNIQUE3RIVER",
  UPDOWNSIDEGAPTHREEMETHODS: "CDLXSIDEGAP3METHODS",
};

const PREFERRED_MAIN_INDICATORS = [
  "ta_MA",
  "ta_EMA",
  "ta_BBANDS",
  "ta_SAR",
  "ta_SAREXT",
  "ta_KAMA",
  "ta_MAMA",
];

const PREFERRED_SUB_INDICATORS = [
  "ta_MACD",
  "ta_RSI",
  "ta_ADX",
  "ta_STOCHRSI",
  "ta_ATR",
  "ta_OBV",
];

export const TA_MAIN_INDICATORS: string[] = ["ta_MA", "ta_EMA", "ta_BBANDS", "ta_SAR"];
export const TA_SUB_INDICATORS: string[] = ["NONE", "ta_MACD", "ta_RSI", "ta_ADX", "ta_STOCHRSI", "ta_ATR", "ta_OBV"];

export const TA_INDICATOR_DEFAULT_PARAMS: Record<string, number[]> = Object.create(null) as Record<string, number[]>;
export const TA_INDICATOR_PARAM_LABELS: Record<string, string[]> = Object.create(null) as Record<string, string[]>;
const TA_INDICATOR_EDITOR_SCHEMAS: Record<string, TalibIndicatorEditorSchema> = Object.create(
  null
) as Record<string, TalibIndicatorEditorSchema>;

let registered = false;
let registerPromise: Promise<void> | null = null;
const runtimeSpecByName = new Map<string, RuntimeIndicatorSpec>();

function normalizeKey(value: string): string {
  return value.trim().toLowerCase().replace(/[^a-z0-9]/g, "");
}

function normalizeInputName(value: string): string {
  return value.trim().toUpperCase().replace(/[^A-Z0-9]/g, "");
}

export function normalizeTalibInputSource(value: string): string {
  const key = value.trim().toUpperCase();
  return TALIB_INPUT_CANONICAL_BY_KEY.get(key) ?? "Close";
}

function isConfigurableInputName(value: string): boolean {
  return value === "INREAL" || value === "INREAL0" || value === "INREAL1";
}

function toParamValueType(typeText: string | undefined): "integer" | "double" | "matype" {
  const normalized = typeText?.trim().toLowerCase() ?? "";
  if (normalized === "integer") {
    return "integer";
  }
  if (normalized === "matype") {
    return "matype";
  }
  return "double";
}

function cloneInputSlots(slots: TalibIndicatorInputSlot[]): TalibIndicatorInputSlot[] {
  return slots.map((slot) => ({
    ...slot,
    options: slot.options.map((option) => ({ ...option })),
  }));
}

function cloneParamDefinitions(definitions: TalibIndicatorParamDefinition[]): TalibIndicatorParamDefinition[] {
  return definitions.map((definition) => ({
    ...definition,
    enumOptions: definition.enumOptions.map((option) => ({ ...option })),
  }));
}

function sortByPreferred(values: string[], preferred: string[]): string[] {
  const priority = new Map<string, number>(preferred.map((name, index) => [name, index]));
  return values.slice().sort((a, b) => {
    const ar = priority.get(a) ?? Number.MAX_SAFE_INTEGER;
    const br = priority.get(b) ?? Number.MAX_SAFE_INTEGER;
    if (ar !== br) {
      return ar - br;
    }
    return a.localeCompare(b);
  });
}

function extractRefKey(rawRef: string): string {
  const parts = rawRef.split("/");
  const key = parts[parts.length - 1];
  return key ?? "";
}

function resolveParamLabels(indicator: TalibConfigIndicator, root: TalibConfigRoot, metaDef: TalibMetaIndicator): string[] {
  const labels: string[] = [];
  const common = root.common ?? {};
  const options = indicator.options ?? [];

  for (const option of options) {
    if (typeof option.key === "string" && option.key.trim().length > 0) {
      labels.push(option.key.trim());
      continue;
    }
    if (typeof option.$ref === "string" && option.$ref.trim().length > 0) {
      const refKey = extractRefKey(option.$ref);
      const label = common[refKey]?.key?.trim();
      labels.push(label && label.length > 0 ? label : refKey);
      continue;
    }
    labels.push("");
  }

  const targetLength = metaDef.options.length;
  if (labels.length < targetLength) {
    for (let i = labels.length; i < targetLength; i += 1) {
      const fallback = metaDef.options[i]?.displayName ?? metaDef.options[i]?.name ?? `Param ${i + 1}`;
      labels.push(fallback);
    }
  }

  return labels.slice(0, targetLength).map((label, index) => {
    const normalized = label.trim();
    if (normalized.length > 0) {
      return normalized;
    }
    const fallback = metaDef.options[index]?.displayName ?? metaDef.options[index]?.name ?? `Param ${index + 1}`;
    return fallback;
  });
}

function resolveDefaultParams(indicator: TalibConfigIndicator, root: TalibConfigRoot, metaDef: TalibMetaIndicator): number[] {
  const defaults = metaDef.options.map((option) =>
    Number.isFinite(option.defaultValue) ? Number(option.defaultValue) : Number.NaN
  );
  const options = indicator.options ?? [];
  const common = root.common ?? {};

  for (let i = 0; i < defaults.length; i += 1) {
    if (Number.isFinite(defaults[i])) {
      continue;
    }
    const option = options[i];
    if (!option || typeof option.$ref !== "string") {
      defaults[i] = 0;
      continue;
    }
    const refKey = extractRefKey(option.$ref);
    const fallback = common[refKey]?.default;
    defaults[i] = Number.isFinite(fallback) ? Number(fallback) : 0;
  }

  return defaults.map((value) => (Number.isFinite(value) ? value : 0));
}

function resolveDefaultInputSeries(indicator: TalibConfigIndicator, metaDef: TalibMetaIndicator): string[] {
  const configured = indicator.inputs?.series ?? [];
  const series: string[] = [];
  for (let i = 0; i < metaDef.inputs.length; i += 1) {
    const inputName = normalizeInputName(metaDef.inputs[i].name);
    if (inputName === "OPEN") {
      series.push("Open");
      continue;
    }
    if (inputName === "HIGH") {
      series.push("High");
      continue;
    }
    if (inputName === "LOW") {
      series.push("Low");
      continue;
    }
    if (inputName === "CLOSE") {
      series.push("Close");
      continue;
    }
    if (inputName === "VOLUME") {
      series.push("Volume");
      continue;
    }
    series.push(normalizeTalibInputSource(configured[i] ?? "Close"));
  }
  return series;
}

function resolveInputSlots(indicator: TalibConfigIndicator, metaDef: TalibMetaIndicator): TalibIndicatorInputSlot[] {
  const configured = indicator.inputs?.series ?? [];
  const slots: TalibIndicatorInputSlot[] = [];
  let displayIndex = 1;
  for (let i = 0; i < metaDef.inputs.length; i += 1) {
    const inputName = normalizeInputName(metaDef.inputs[i].name);
    if (!isConfigurableInputName(inputName)) {
      continue;
    }
    const defaultValue = normalizeTalibInputSource(configured[i] ?? "Close");
    slots.push({
      key: metaDef.inputs[i].name,
      label: displayIndex === 1 ? "Input Source" : `Input Source ${displayIndex}`,
      defaultValue,
      options: TALIB_INPUT_OPTIONS.map((option) => ({ ...option })),
    });
    displayIndex += 1;
  }
  return slots;
}

function resolveParamDefinitions(
  indicator: TalibConfigIndicator,
  root: TalibConfigRoot,
  metaDef: TalibMetaIndicator,
  defaultParams: number[],
  paramLabels: string[]
): TalibIndicatorParamDefinition[] {
  const common = root.common ?? {};
  const options = indicator.options ?? [];
  const definitions: TalibIndicatorParamDefinition[] = [];

  for (let i = 0; i < metaDef.options.length; i += 1) {
    const metaOption = metaDef.options[i];
    const configOption = options[i];
    const valueType = toParamValueType(metaOption.type);
    const defaultValue = Number.isFinite(defaultParams[i]) ? Number(defaultParams[i]) : 0;
    const label =
      paramLabels[i] ??
      configOption?.key?.trim() ??
      metaOption.displayName ??
      metaOption.name ??
      `Param ${i + 1}`;

    let description = configOption?.desc?.trim();
    let enumOptions: TalibIndicatorParamEnumOption[] = [];

    if (typeof configOption?.$ref === "string" && configOption.$ref.trim().length > 0) {
      const refKey = extractRefKey(configOption.$ref);
      const ref = common[refKey];
      if (typeof ref?.desc === "string" && ref.desc.trim().length > 0) {
        description = ref.desc.trim();
      }
      if (Array.isArray(ref?.enum)) {
        for (const item of ref.enum) {
          const value = Number(item.value);
          if (!Number.isFinite(value)) {
            continue;
          }
          const labelText = item.label?.trim();
          if (!labelText) {
            continue;
          }
          enumOptions.push({
            label: labelText,
            value,
            description: item.name?.trim() || undefined,
          });
        }
      }
    }

    const type: "number" | "enum" = enumOptions.length > 0 ? "enum" : "number";
    definitions.push({
      key: metaOption.name,
      label,
      description,
      type,
      valueType,
      defaultValue,
      enumOptions,
    });
  }

  return definitions;
}

function resolveSeries(indicator: TalibConfigIndicator, metaDef: TalibMetaIndicator): IndicatorSeries {
  const groupText = `${indicator.indicator_type ?? indicator.group ?? metaDef.group ?? ""}`.toLowerCase();
  if (groupText.includes("overlap studies") || groupText.includes("price transform")) {
    return IndicatorSeries.Price;
  }
  return IndicatorSeries.Normal;
}

function resolveShouldFormatBigNumber(indicator: TalibConfigIndicator, metaDef: TalibMetaIndicator): boolean {
  const groupText = `${indicator.indicator_type ?? indicator.group ?? metaDef.group ?? ""}`.toLowerCase();
  return groupText.includes("volume");
}

function resolveRange(talibCode: string): { minValue: number | null; maxValue: number | null } {
  if (talibCode === "RSI" || talibCode === "STOCH" || talibCode === "STOCHF" || talibCode === "STOCHRSI") {
    return { minValue: 0, maxValue: 100 };
  }
  if (talibCode === "WILLR") {
    return { minValue: -100, maxValue: 0 };
  }
  return { minValue: null, maxValue: null };
}

function toFigureTitle(outputLabel: string): string {
  return `${outputLabel}: `;
}

function toFigureType(backendCode: string, rawHint: string): "line" | "bar" | "circle" {
  const hint = rawHint.toLowerCase();
  if (backendCode === "SAR" || backendCode === "SAREXT") {
    return "circle";
  }
  if (hint.includes("histogram") || hint.includes("bar")) {
    return "bar";
  }
  if (hint.includes("dot") || hint.includes("point") || hint.includes("circle")) {
    return "circle";
  }
  return "line";
}

function readIndicatorValue(
  source: IndicatorFigureStylesCallbackData<TalibCalcResult>["current"] | IndicatorFigureStylesCallbackData<TalibCalcResult>["prev"],
  key: string
): number {
  const value = source.indicatorData?.[key];
  return typeof value === "number" && Number.isFinite(value) ? value : 0;
}

function createHistogramFigureStyles(
  key: string
): ((
  data: IndicatorFigureStylesCallbackData<TalibCalcResult>,
  _indicator: Indicator<TalibCalcResult>,
  _defaultStyles: unknown
) => IndicatorFigureStyle) {
  return (data) => {
    const currentValue = readIndicatorValue(data.current, key);
    const previousValue = readIndicatorValue(data.prev, key);
    const color = currentValue >= 0 ? "#ef5350" : "#26a69a";
    const style = previousValue <= currentValue ? PolygonType.Stroke : PolygonType.Fill;
    return {
      color,
      borderColor: color,
      style,
    };
  };
}

function buildFigures(indicator: TalibConfigIndicator, metaDef: TalibMetaIndicator): Array<IndicatorFigure<TalibCalcResult>> {
  const outputs = metaDef.outputs;
  const configuredOutputs = indicator.outputs ?? [];
  const figures: Array<IndicatorFigure<TalibCalcResult>> = [];

  for (let i = 0; i < outputs.length; i += 1) {
    const output = outputs[i];
    const configured = configuredOutputs[i];
    const outputLabel = configured?.key?.trim() || output.name;
    const hint = `${configured?.hint ?? ""} ${output.plotHint ?? ""}`;
    const type = toFigureType(indicator.code, hint);

    if (type === "bar") {
      figures.push({
        key: output.name,
        title: toFigureTitle(outputLabel),
        type,
        baseValue: 0,
        styles: createHistogramFigureStyles(output.name),
      });
      continue;
    }

    figures.push({
      key: output.name,
      title: toFigureTitle(outputLabel),
      type,
    });
  }

  return figures;
}

function resolveTalibCode(indicator: TalibConfigIndicator, meta: TalibMetaRoot): string | null {
  const codeMap = new Map<string, string>();
  const camelMap = new Map<string, string>();
  const descriptionMap = new Map<string, string>();

  for (const [code, def] of Object.entries(meta)) {
    codeMap.set(normalizeKey(code), code);
    if (typeof def.camelCaseName === "string" && def.camelCaseName.length > 0) {
      camelMap.set(normalizeKey(def.camelCaseName), code);
    }
    if (typeof def.description === "string" && def.description.length > 0) {
      descriptionMap.set(normalizeKey(def.description), code);
    }
  }

  const candidates: string[] = [];
  const upperCode = indicator.code.toUpperCase();
  if (TALIB_CODE_ALIAS[upperCode]) {
    candidates.push(TALIB_CODE_ALIAS[upperCode]);
  }
  candidates.push(indicator.code);
  candidates.push(`CDL${indicator.code}`);
  if (indicator.method) {
    candidates.push(indicator.method);
    candidates.push(`cdl${indicator.method}`);
  }
  if (indicator.name_en) {
    candidates.push(indicator.name_en);
  }

  for (const candidate of candidates) {
    const normalized = normalizeKey(candidate);
    if (normalized.length === 0) {
      continue;
    }
    const direct = codeMap.get(normalized);
    if (direct) {
      return direct;
    }
    const camel = camelMap.get(normalized);
    if (camel) {
      return camel;
    }
    const byDescription = descriptionMap.get(normalized);
    if (byDescription) {
      return byDescription;
    }
  }

  return null;
}

function buildRuntimeSpecs(root: TalibConfigRoot, meta: TalibMetaRoot): {
  specs: RuntimeIndicatorSpec[];
  unresolvedCodes: string[];
} {
  const specs: RuntimeIndicatorSpec[] = [];
  const unresolvedCodes: string[] = [];

  for (const indicator of root.indicators) {
    const talibCode = resolveTalibCode(indicator, meta);
    if (!talibCode) {
      unresolvedCodes.push(indicator.code);
      continue;
    }
    const metaDef = meta[talibCode];
    if (!metaDef) {
      unresolvedCodes.push(indicator.code);
      continue;
    }

    const defaultParams = resolveDefaultParams(indicator, root, metaDef);
    const paramLabels = resolveParamLabels(indicator, root, metaDef);
    const paramDefinitions = resolveParamDefinitions(indicator, root, metaDef, defaultParams, paramLabels);
    const inputSlots = resolveInputSlots(indicator, metaDef);
    const defaultInputSeries = resolveDefaultInputSeries(indicator, metaDef);
    const figures = buildFigures(indicator, metaDef);
    const series = resolveSeries(indicator, metaDef);
    const range = resolveRange(talibCode);
    const calcSpec: TalibRuntimeCalcSpec = {
      code: talibCode,
      inputSeries: defaultInputSeries,
      inputs: metaDef.inputs.map((input) => ({ name: input.name })),
      options: metaDef.options.map((option) => ({
        name: option.name,
        type: option.type,
        defaultValue: option.defaultValue,
      })),
      outputs: metaDef.outputs.map((output) => ({ key: output.name })),
    };

    specs.push({
      name: `ta_${indicator.code}`,
      backendCode: indicator.code,
      talibCode,
      group: indicator.indicator_type ?? indicator.group ?? metaDef.group ?? "Other",
      series,
      shouldOhlc: series === IndicatorSeries.Price,
      shouldFormatBigNumber: resolveShouldFormatBigNumber(indicator, metaDef),
      minValue: range.minValue,
      maxValue: range.maxValue,
      defaultParams,
      paramLabels,
      paramDefinitions,
      inputSlots,
      figures,
      calcSpec,
    });
  }

  return { specs, unresolvedCodes };
}

function resetCollections(): void {
  for (const key of Object.keys(TA_INDICATOR_DEFAULT_PARAMS)) {
    delete TA_INDICATOR_DEFAULT_PARAMS[key];
  }
  for (const key of Object.keys(TA_INDICATOR_PARAM_LABELS)) {
    delete TA_INDICATOR_PARAM_LABELS[key];
  }
  for (const key of Object.keys(TA_INDICATOR_EDITOR_SCHEMAS)) {
    delete TA_INDICATOR_EDITOR_SCHEMAS[key];
  }
  TA_MAIN_INDICATORS.length = 0;
  TA_SUB_INDICATORS.length = 0;
  TA_SUB_INDICATORS.push("NONE");
  runtimeSpecByName.clear();
}

function populateCollections(specs: RuntimeIndicatorSpec[]): void {
  resetCollections();

  const main: string[] = [];
  const sub: string[] = [];

  for (const spec of specs) {
    runtimeSpecByName.set(spec.name, spec);
    TA_INDICATOR_DEFAULT_PARAMS[spec.name] = [...spec.defaultParams];
    TA_INDICATOR_PARAM_LABELS[spec.name] = [...spec.paramLabels];
    TA_INDICATOR_EDITOR_SCHEMAS[spec.name] = {
      name: spec.name,
      code: spec.backendCode,
      talibCode: spec.talibCode,
      group: spec.group,
      pane: spec.series === IndicatorSeries.Price ? "main" : "sub",
      inputSlots: cloneInputSlots(spec.inputSlots),
      paramDefinitions: cloneParamDefinitions(spec.paramDefinitions),
      defaultParams: [...spec.defaultParams],
      paramLabels: [...spec.paramLabels],
    };
    if (spec.series === IndicatorSeries.Price) {
      main.push(spec.name);
    } else {
      sub.push(spec.name);
    }
  }

  const sortedMain = sortByPreferred(main, PREFERRED_MAIN_INDICATORS);
  const sortedSub = sortByPreferred(sub, PREFERRED_SUB_INDICATORS);
  TA_MAIN_INDICATORS.push(...sortedMain);
  TA_SUB_INDICATORS.push(...sortedSub);
}

async function loadConfig(): Promise<TalibConfigRoot> {
  const response = await fetch("/talib_indicators_config.json", { cache: "no-cache" });
  if (!response.ok) {
    throw new Error(`load talib config failed: HTTP ${response.status}`);
  }
  return (await response.json()) as TalibConfigRoot;
}

async function loadMeta(): Promise<TalibMetaRoot> {
  const response = await fetch("/talib_web_api_meta.json", { cache: "no-cache" });
  if (!response.ok) {
    throw new Error(`load talib meta failed: HTTP ${response.status}`);
  }
  return (await response.json()) as TalibMetaRoot;
}

export async function registerTalibIndicators(): Promise<void> {
  if (registered) {
    return;
  }
  if (registerPromise) {
    await registerPromise;
    return;
  }

  registerPromise = (async () => {
    await ensureTalibReady("/talib.wasm");
    const [configRoot, metaRoot] = await Promise.all([loadConfig(), loadMeta()]);
    const { specs, unresolvedCodes } = buildRuntimeSpecs(configRoot, metaRoot);
    populateCollections(specs);

    const supported = new Set(getSupportedIndicators());
    for (const spec of specs) {
      if (supported.has(spec.name)) {
        continue;
      }
      const indicatorTemplate: IndicatorTemplate<TalibCalcResult> = {
        name: spec.name,
        shortName: spec.name,
        series: spec.series,
        shouldOhlc: spec.shouldOhlc,
        shouldFormatBigNumber: spec.shouldFormatBigNumber,
        calcParams: [...spec.defaultParams],
        minValue: spec.minValue,
        maxValue: spec.maxValue,
        figures: spec.figures,
        calc: (kLineDataList: KLineData[], indicator: Indicator<TalibCalcResult>) => {
          const calcParams = Array.isArray(indicator.calcParams)
            ? (indicator.calcParams as number[])
            : spec.defaultParams;
          return calcTalibIndicator(spec.calcSpec, kLineDataList, calcParams, indicator.extendData);
        },
      };
      registerIndicator(indicatorTemplate);
    }

    if (unresolvedCodes.length > 0) {
      console.warn("[talib] unresolved indicator mappings:", unresolvedCodes.join(", "));
    }
    registered = true;
  })();

  try {
    await registerPromise;
  } finally {
    if (!registered) {
      registerPromise = null;
    }
  }
}

export function getTalibDefaultParams(indicatorName: string): number[] {
  return [...(TA_INDICATOR_DEFAULT_PARAMS[indicatorName] ?? [])];
}

export function getTalibIndicatorParamConfig(indicatorName: string): TalibIndicatorParamConfig | null {
  const defaultParams = TA_INDICATOR_DEFAULT_PARAMS[indicatorName];
  const paramLabels = TA_INDICATOR_PARAM_LABELS[indicatorName];
  if (!defaultParams || !paramLabels) {
    return null;
  }
  return { defaultParams: [...defaultParams], paramLabels: [...paramLabels] };
}

export function getTalibInputOptions(): TalibIndicatorInputOption[] {
  return TALIB_INPUT_OPTIONS.map((option) => ({ ...option }));
}

export function getTalibIndicatorEditorSchema(indicatorName: string): TalibIndicatorEditorSchema | null {
  const schema = TA_INDICATOR_EDITOR_SCHEMAS[indicatorName];
  if (!schema) {
    return null;
  }
  return {
    ...schema,
    inputSlots: cloneInputSlots(schema.inputSlots),
    paramDefinitions: cloneParamDefinitions(schema.paramDefinitions),
    defaultParams: [...schema.defaultParams],
    paramLabels: [...schema.paramLabels],
  };
}

export function getTalibIndicatorNames(): string[] {
  return Object.keys(TA_INDICATOR_DEFAULT_PARAMS).sort((a, b) => a.localeCompare(b));
}

export function getTalibIndicatorMetaList(): TalibRegisteredIndicatorMeta[] {
  const list: TalibRegisteredIndicatorMeta[] = [];
  for (const spec of runtimeSpecByName.values()) {
    list.push({
      name: spec.name,
      code: spec.backendCode,
      talibCode: spec.talibCode,
      pane: spec.series === IndicatorSeries.Price ? "main" : "sub",
      group: spec.group,
    });
  }
  return list.sort((a, b) => {
    if (a.pane !== b.pane) {
      return a.pane.localeCompare(b.pane);
    }
    if (a.group !== b.group) {
      return a.group.localeCompare(b.group);
    }
    return a.name.localeCompare(b.name);
  });
}
