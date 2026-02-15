/**
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at

 * http://www.apache.org/licenses/LICENSE-2.0

 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

import type Nullable from '../common/Nullable'
import type KLineData from '../common/KLineData'
import type Crosshair from '../common/Crosshair'
import { type IndicatorStyle, type TooltipStyle, type TooltipIconStyle, type TooltipTextStyle, type TooltipLegend, TooltipShowRule, type TooltipLegendChild, TooltipIconPosition } from '../common/Styles'
import { ActionType } from '../common/Action'
import { formatPrecision, formatThousands, formatFoldDecimal } from '../common/utils/format'
import { isValid, isObject, isString, isNumber } from '../common/utils/typeChecks'
import { createFont } from '../common/utils/canvas'
import type Coordinate from '../common/Coordinate'

import { type CustomApi } from '../Options'

import type YAxis from '../component/YAxis'

import { type Indicator, type IndicatorFigure, type IndicatorFigureStyle, type IndicatorTooltipData } from '../component/Indicator'
import type IndicatorImp from '../component/Indicator'
import { eachFigures } from '../component/Indicator'

import { type TooltipIcon, type TooltipIndicator } from '../store/TooltipStore'

import View from './View'

export default class IndicatorTooltipView extends View<YAxis> {
  private readonly _boundIconClickEvent = (currentIcon: TooltipIcon) => () => {
    const pane = this.getWidget().getPane()
    pane.getChart().getChartStore().getActionStore().execute(ActionType.OnTooltipIconClick, { ...currentIcon })
    return true
  }

  private readonly _boundIconMouseMoveEvent = (currentIconInfo: TooltipIcon) => () => {
    const pane = this.getWidget().getPane()
    const chartStore = pane.getChart().getChartStore()
    const tooltipStore = chartStore.getTooltipStore()
    tooltipStore.setActiveIcon({ ...currentIconInfo })
    chartStore.getActionStore().execute(ActionType.OnTooltipIconHover, { ...currentIconInfo })
    return true
  }

  private readonly _boundIndicatorTooltipMouseMoveEvent = (currentIndicator: TooltipIndicator) => () => {
    const pane = this.getWidget().getPane()
    const chartStore = pane.getChart().getChartStore()
    const tooltipStore = chartStore.getTooltipStore()
    tooltipStore.setActiveTooltipIndicator({ ...currentIndicator })
    chartStore.getActionStore().execute(ActionType.OnIndicatorTooltipHover, { ...currentIndicator })
    return true
  }

  override drawImp (ctx: CanvasRenderingContext2D): void {
    const widget = this.getWidget()
    const pane = widget.getPane()
    const chartStore = pane.getChart().getChartStore()
    const crosshair = chartStore.getTooltipStore().getCrosshair()
    if (isValid(crosshair.kLineData)) {
      const bounding = widget.getBounding()
      const customApi = chartStore.getCustomApi()
      const thousandsSeparator = chartStore.getThousandsSeparator()
      const decimalFoldThreshold = chartStore.getDecimalFoldThreshold()
      const indicators = chartStore.getIndicatorStore().getInstances(pane.getId())
      const activeIcon = chartStore.getTooltipStore().getActiveIcon()
      const activeIndicator = chartStore.getTooltipStore().getActiveTooltipIndicator()
      const defaultStyles = chartStore.getStyles().indicator
      const { offsetLeft, offsetTop, offsetRight } = defaultStyles.tooltip
      this.drawIndicatorTooltip(
        ctx, pane.getId(), chartStore.getDataList(),
        crosshair, activeIcon, activeIndicator, indicators, customApi,
        thousandsSeparator, decimalFoldThreshold,
        offsetLeft, offsetTop,
        bounding.width - offsetRight, defaultStyles
      )
    }
  }

  protected drawIndicatorTooltip (
    ctx: CanvasRenderingContext2D,
    paneId: string,
    dataList: KLineData[],
    crosshair: Crosshair,
    activeTooltipIcon: Nullable<TooltipIcon>,
    activeIndicator: Nullable<TooltipIndicator>,
    indicators: IndicatorImp[],
    customApi: CustomApi,
    thousandsSeparator: string,
    decimalFoldThreshold: number,
    left: number,
    top: number,
    maxWidth: number,
    styles: IndicatorStyle
  ): number {
    const tooltipStyles = styles.tooltip
    if (this.isDrawTooltip(crosshair, tooltipStyles)) {
      const tooltipTextStyles = tooltipStyles.text
      indicators.forEach(indicator => {
        let prevRowHeight = 0
        const coordinate = { x: left, y: top }
        const { name, calcParamsText, values: legends, icons } = this.getIndicatorTooltipData(dataList, crosshair, indicator, customApi, thousandsSeparator, decimalFoldThreshold, styles)
        const nameValid = name.length > 0
        const legendValid = legends.length > 0
        if (nameValid || legendValid) {
          const [leftIconsRaw, middleIconsRaw, rightIconsRaw] = this.classifyTooltipIcons(icons)
          const showIcons = activeIndicator?.paneId === paneId && activeIndicator?.indicatorName === indicator.name
          const showLegends = legendValid && !showIcons
          const leftIcons = showIcons ? leftIconsRaw : []
          const middleIcons = showIcons ? middleIconsRaw : []
          const rightIcons = showIcons ? rightIconsRaw : []
          const rowTop = coordinate.y
          prevRowHeight = this.drawStandardTooltipIcons(
            ctx, activeTooltipIcon, leftIcons,
            coordinate, paneId, indicator.name,
            left, prevRowHeight, maxWidth
          )

          if (nameValid) {
            let text = name
            if (calcParamsText.length > 0) {
              text = `${text}${calcParamsText}`
            }
            prevRowHeight = this.drawStandardTooltipLegends(
              ctx,
              [
                {
                  title: { text: '', color: tooltipTextStyles.color },
                  value: { text, color: tooltipTextStyles.color }
                }
              ],
              coordinate, left, prevRowHeight, maxWidth, tooltipTextStyles
            )
          }

          prevRowHeight = this.drawStandardTooltipIcons(
            ctx, activeTooltipIcon, middleIcons,
            coordinate, paneId, indicator.name,
            left, prevRowHeight, maxWidth
          )

          if (showLegends) {
            prevRowHeight = this.drawStandardTooltipLegends(
              ctx, legends, coordinate,
              left, prevRowHeight, maxWidth, tooltipStyles.text
            )
          }

          // draw right icons
          prevRowHeight = this.drawStandardTooltipIcons(
            ctx, activeTooltipIcon, rightIcons,
            coordinate, paneId, indicator.name,
            left, prevRowHeight, maxWidth
          )
          const rowBottom = coordinate.y + prevRowHeight
          const maxRowWidth = Math.max(maxWidth - left, 0)
          const rowUsedWidth = Math.max(coordinate.x - left, 0)
          const rowWidth = Math.min(maxRowWidth, Math.max(rowUsedWidth + 12, showIcons ? 120 : 160))
          if (rowWidth > 0 && rowBottom > rowTop) {
            // Draw behind text/icons without affecting existing paint order.
            ctx.save()
            ctx.globalCompositeOperation = 'destination-over'
            ctx.fillStyle = showIcons ? 'rgba(59, 130, 246, 0.16)' : 'rgba(0, 0, 0, 0)'
            ctx.fillRect(left, rowTop, rowWidth, rowBottom - rowTop)
            ctx.strokeStyle = showIcons ? 'rgba(37, 99, 235, 0.42)' : 'rgba(0, 0, 0, 0)'
            ctx.lineWidth = 1
            ctx.strokeRect(
              left + 0.5,
              rowTop + 0.5,
              Math.max(rowWidth - 1, 0),
              Math.max(rowBottom - rowTop - 1, 0)
            )
            ctx.restore()
          }
          this.createFigure({
            name: 'rect',
            attrs: {
              x: left,
              y: rowTop,
              width: Math.max(maxWidth - left, 0),
              height: Math.max(prevRowHeight, 0)
            },
            styles: {
              color: 'rgba(0, 0, 0, 0)',
              borderColor: 'rgba(0, 0, 0, 0)',
              borderSize: 0
            }
          }, {
            mouseMoveEvent: this._boundIndicatorTooltipMouseMoveEvent({ paneId, indicatorName: indicator.name })
          })?.draw(ctx)
          top = coordinate.y + prevRowHeight
        }
      })
    }
    return top
  }

  protected drawStandardTooltipIcons (
    ctx: CanvasRenderingContext2D,
    activeIcon: Nullable<TooltipIcon>,
    icons: TooltipIconStyle[],
    coordinate: Coordinate,
    paneId: string,
    indicatorName: string,
    left: number,
    prevRowHeight: number,
    maxWidth: number
  ): number {
    if (icons.length > 0) {
      let width = 0
      let height = 0
      icons.forEach(icon => {
        const {
          marginLeft = 0, marginTop = 0, marginRight = 0, marginBottom = 0,
          paddingLeft = 0, paddingTop = 0, paddingRight = 0, paddingBottom = 0,
          size
        } = icon
        const contentWidth = this.getTooltipIconContentWidth(ctx, icon)
        width += (marginLeft + paddingLeft + contentWidth + paddingRight + marginRight)
        height = Math.max(height, marginTop + paddingTop + size + paddingBottom + marginBottom)
      })
      if (coordinate.x + width > maxWidth) {
        coordinate.x = left
        coordinate.y += prevRowHeight
        prevRowHeight = height
      } else {
        prevRowHeight = Math.max(prevRowHeight, height)
      }
      icons.forEach(icon => {
        const {
          marginLeft = 0, marginTop = 0, marginRight = 0,
          paddingLeft = 0, paddingTop = 0, paddingRight = 0, paddingBottom = 0,
          color, activeColor, size, fontFamily, icon: iconToken,
          backgroundColor, activeBackgroundColor
        } = icon
        const active = activeIcon?.paneId === paneId && activeIcon?.indicatorName === indicatorName && activeIcon?.iconId === icon.id
        const contentWidth = this.getTooltipIconContentWidth(ctx, icon)
        const iconX = coordinate.x + marginLeft
        const iconY = coordinate.y + marginTop
        const iconWidth = paddingLeft + contentWidth + paddingRight
        const iconHeight = paddingTop + size + paddingBottom
        const iconColor = active ? activeColor : color
        const iconBackgroundColor = active ? activeBackgroundColor : backgroundColor
        if (this.isSvgTooltipActionIcon(iconToken)) {
          this.createFigure({
            name: 'rect',
            attrs: { x: iconX, y: iconY, width: iconWidth, height: iconHeight },
            styles: {
              color: iconBackgroundColor,
              borderColor: 'rgba(0, 0, 0, 0)',
              borderSize: 0,
              borderRadius: 4
            }
          }, {
            mouseClickEvent: this._boundIconClickEvent({ paneId, indicatorName, iconId: icon.id }),
            mouseMoveEvent: this._boundIconMouseMoveEvent({ paneId, indicatorName, iconId: icon.id })
          })?.draw(ctx)
          this.drawSvgTooltipActionIcon(
            ctx, iconToken,
            iconX + paddingLeft, iconY + paddingTop,
            size, iconColor
          )
        } else {
          this.createFigure({
            name: 'text',
            attrs: { text: iconToken, x: iconX, y: iconY },
            styles: {
              paddingLeft,
              paddingTop,
              paddingRight,
              paddingBottom,
              color: iconColor,
              size,
              family: fontFamily,
              backgroundColor: iconBackgroundColor
            }
          }, {
            mouseClickEvent: this._boundIconClickEvent({ paneId, indicatorName, iconId: icon.id }),
            mouseMoveEvent: this._boundIconMouseMoveEvent({ paneId, indicatorName, iconId: icon.id })
          })?.draw(ctx)
        }
        coordinate.x += (marginLeft + iconWidth + marginRight)
      })
    }
    return prevRowHeight
  }

  private isSvgTooltipActionIcon (iconToken: string): boolean {
    return iconToken === 'svg:hide' || iconToken === 'svg:settings' || iconToken === 'svg:delete'
  }

  private getTooltipIconContentWidth (ctx: CanvasRenderingContext2D, icon: TooltipIconStyle): number {
    const { size, fontFamily, icon: iconToken } = icon
    if (this.isSvgTooltipActionIcon(iconToken)) {
      return size
    }
    ctx.font = createFont(size, 'normal', fontFamily)
    return ctx.measureText(iconToken).width
  }

  private drawSvgTooltipActionIcon (
    ctx: CanvasRenderingContext2D,
    iconToken: string,
    x: number,
    y: number,
    size: number,
    color: string
  ): void {
    ctx.save()
    ctx.strokeStyle = color
    ctx.fillStyle = color
    ctx.lineWidth = Math.max(1, size * 0.1)
    ctx.lineCap = 'round'
    ctx.lineJoin = 'round'
    switch (iconToken) {
      case 'svg:hide': {
        this.drawSvgHideIcon(ctx, x, y, size)
        break
      }
      case 'svg:settings': {
        this.drawSvgSettingsIcon(ctx, x, y, size)
        break
      }
      case 'svg:delete': {
        this.drawSvgDeleteIcon(ctx, x, y, size)
        break
      }
      default: {
        break
      }
    }
    ctx.restore()
  }

  private drawSvgHideIcon (ctx: CanvasRenderingContext2D, x: number, y: number, size: number): void {
    const cx = x + size / 2
    const cy = y + size / 2
    const radiusX = size * 0.34
    const radiusY = size * 0.22
    ctx.beginPath()
    ctx.ellipse(cx, cy, radiusX, radiusY, 0, 0, Math.PI * 2)
    ctx.stroke()
    ctx.beginPath()
    ctx.arc(cx, cy, size * 0.09, 0, Math.PI * 2)
    ctx.fill()
    ctx.beginPath()
    ctx.moveTo(x + size * 0.18, y + size * 0.82)
    ctx.lineTo(x + size * 0.82, y + size * 0.18)
    ctx.stroke()
  }

  private drawSvgSettingsIcon (ctx: CanvasRenderingContext2D, x: number, y: number, size: number): void {
    const cx = x + size / 2
    const cy = y + size / 2
    const spokeInner = size * 0.28
    const spokeOuter = size * 0.44
    for (let i = 0; i < 6; i++) {
      const angle = i * Math.PI / 3
      const sx = cx + Math.cos(angle) * spokeInner
      const sy = cy + Math.sin(angle) * spokeInner
      const ex = cx + Math.cos(angle) * spokeOuter
      const ey = cy + Math.sin(angle) * spokeOuter
      ctx.beginPath()
      ctx.moveTo(sx, sy)
      ctx.lineTo(ex, ey)
      ctx.stroke()
    }
    ctx.beginPath()
    ctx.arc(cx, cy, size * 0.24, 0, Math.PI * 2)
    ctx.stroke()
    ctx.beginPath()
    ctx.arc(cx, cy, size * 0.09, 0, Math.PI * 2)
    ctx.fill()
  }

  private drawSvgDeleteIcon (ctx: CanvasRenderingContext2D, x: number, y: number, size: number): void {
    const bodyX = x + size * 0.24
    const bodyY = y + size * 0.34
    const bodyWidth = size * 0.52
    const bodyHeight = size * 0.44
    ctx.beginPath()
    ctx.moveTo(x + size * 0.18, y + size * 0.30)
    ctx.lineTo(x + size * 0.82, y + size * 0.30)
    ctx.stroke()
    ctx.beginPath()
    ctx.moveTo(x + size * 0.42, y + size * 0.18)
    ctx.lineTo(x + size * 0.58, y + size * 0.18)
    ctx.stroke()
    ctx.strokeRect(bodyX, bodyY, bodyWidth, bodyHeight)
    const gap = bodyWidth / 4
    for (let i = 1; i <= 2; i++) {
      const lineX = bodyX + gap * i
      ctx.beginPath()
      ctx.moveTo(lineX, bodyY + size * 0.07)
      ctx.lineTo(lineX, bodyY + bodyHeight - size * 0.07)
      ctx.stroke()
    }
  }

  protected drawStandardTooltipLegends (
    ctx: CanvasRenderingContext2D,
    legends: TooltipLegend[],
    coordinate: Coordinate,
    left: number,
    prevRowHeight: number,
    maxWidth: number,
    styles: TooltipTextStyle
  ): number {
    if (legends.length > 0) {
      const { marginLeft, marginTop, marginRight, marginBottom, size, family, weight } = styles
      ctx.font = createFont(size, weight, family)
      legends.forEach(data => {
        const title = data.title as TooltipLegendChild
        const value = data.value as TooltipLegendChild
        const titleTextWidth = ctx.measureText(title.text).width
        const valueTextWidth = ctx.measureText(value.text).width
        const totalTextWidth = titleTextWidth + valueTextWidth
        const h = marginTop + size + marginBottom
        if (coordinate.x + marginLeft + totalTextWidth + marginRight > maxWidth) {
          coordinate.x = left
          coordinate.y += prevRowHeight
          prevRowHeight = h
        } else {
          prevRowHeight = Math.max(prevRowHeight, h)
        }
        if (title.text.length > 0) {
          this.createFigure({
            name: 'text',
            attrs: { x: coordinate.x + marginLeft, y: coordinate.y + marginTop, text: title.text },
            styles: { color: title.color, size, family, weight }
          })?.draw(ctx)
        }
        this.createFigure({
          name: 'text',
          attrs: { x: coordinate.x + marginLeft + titleTextWidth, y: coordinate.y + marginTop, text: value.text },
          styles: { color: value.color, size, family, weight }
        })?.draw(ctx)
        coordinate.x += (marginLeft + totalTextWidth + marginRight)
      })
    }
    return prevRowHeight
  }

  protected isDrawTooltip (crosshair: Crosshair, styles: TooltipStyle): boolean {
    const showRule = styles.showRule
    return showRule === TooltipShowRule.Always ||
      (showRule === TooltipShowRule.FollowCross && isString(crosshair.paneId))
  }

  protected getIndicatorTooltipData (
    dataList: KLineData[],
    crosshair: Crosshair,
    indicator: Indicator,
    customApi: CustomApi,
    thousandsSeparator: string,
    decimalFoldThreshold: number,
    styles: IndicatorStyle
  ): IndicatorTooltipData {
    const tooltipStyles = styles.tooltip
    const name = tooltipStyles.showName ? indicator.shortName : ''
    let calcParamsText = ''
    const calcParams = indicator.calcParams
    if (calcParams.length > 0 && tooltipStyles.showParams) {
      calcParamsText = `(${calcParams.join(',')})`
    }

    const tooltipData: IndicatorTooltipData = { name, calcParamsText, values: [], icons: tooltipStyles.icons }

    const dataIndex = crosshair.dataIndex!
    const result = indicator.result ?? []

    const legends: TooltipLegend[] = []
    if (indicator.visible) {
      const indicatorData = result[dataIndex] ?? {}
      eachFigures(dataList, indicator, dataIndex, styles, (figure: IndicatorFigure, figureStyles: Required<IndicatorFigureStyle>) => {
        if (isString(figure.title)) {
          const color = figureStyles.color
          let value = indicatorData[figure.key] ?? tooltipStyles.defaultValue
          if (isNumber(value)) {
            value = formatPrecision(value, indicator.precision)
            if (indicator.shouldFormatBigNumber) {
              value = customApi.formatBigNumber(value as string)
            }
            value = formatFoldDecimal(formatThousands(value as string, thousandsSeparator), decimalFoldThreshold)
          }
          legends.push({ title: { text: figure.title, color }, value: { text: value, color } })
        }
      })
      tooltipData.values = legends
    }

    if (indicator.createTooltipDataSource !== null) {
      const widget = this.getWidget()
      const pane = widget.getPane()
      const chartStore = pane.getChart().getChartStore()
      const { name: customName, calcParamsText: customCalcParamsText, values: customLegends, icons: customIcons } = indicator.createTooltipDataSource({
        kLineDataList: dataList,
        indicator,
        visibleRange: chartStore.getTimeScaleStore().getVisibleRange(),
        bounding: widget.getBounding(),
        crosshair,
        defaultStyles: styles,
        xAxis: pane.getChart().getXAxisPane().getAxisComponent(),
        yAxis: pane.getAxisComponent()
      })
      if (isString(customName) && tooltipStyles.showName) {
        tooltipData.name = customName
      }
      if (isString(customCalcParamsText) && tooltipStyles.showParams) {
        tooltipData.calcParamsText = customCalcParamsText
      }
      if (isValid(customIcons)) {
        tooltipData.icons = customIcons
      }
      if (isValid(customLegends) && indicator.visible) {
        const optimizedLegends: TooltipLegend[] = []
        const color = styles.tooltip.text.color
        customLegends.forEach(data => {
          let title = { text: '', color }
          if (isObject(data.title)) {
            title = data.title
          } else {
            title.text = data.title
          }
          let value = { text: '', color }
          if (isObject(data.value)) {
            value = data.value
          } else {
            value.text = data.value ?? tooltipStyles.defaultValue
          }
          if (isNumber(value.text)) {
            let text = formatPrecision(value.text, indicator.precision)
            if (indicator.shouldFormatBigNumber) {
              text = customApi.formatBigNumber(text)
            }
            text = formatFoldDecimal(formatThousands(text, thousandsSeparator), decimalFoldThreshold)
            value.text = text
          }
          optimizedLegends.push({ title, value })
        })
        tooltipData.values = optimizedLegends
      }
    }
    return tooltipData
  }

  protected classifyTooltipIcons (icons: TooltipIconStyle[]): TooltipIconStyle[][] {
    const leftIcons: TooltipIconStyle[] = []
    const middleIcons: TooltipIconStyle[] = []
    const rightIcons: TooltipIconStyle[] = []
    icons.forEach(icon => {
      switch (icon.position) {
        case TooltipIconPosition.Left: {
          leftIcons.push(icon)
          break
        }
        case TooltipIconPosition.Middle: {
          middleIcons.push(icon)
          break
        }
        case TooltipIconPosition.Right: {
          rightIcons.push(icon)
          break
        }
      }
    })
    return [leftIcons, middleIcons, rightIcons]
  }
}
