<?xml version="1.0" encoding="UTF-8"?>
<!-- ENTSO-E A71/A01: day-ahead generation forecast. The shared GL traversal
     emits timestamp_utc/resolution/area; this stylesheet adds the forecast
     kind (direction, relabeled) and the value. -->
<xsl:stylesheet version="1.0"
    xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
    xmlns:entsoe="urn:iec62325.351:tc57wg16:451-6:generationloaddocument:3:0"
    exclude-result-prefixes="entsoe">
  <xsl:include href="lib/entsoe-gl-document.xslt"/>

  <xsl:template match="entsoe:Point" mode="endpoint-cells">
    <cell name="kind">
      <xsl:call-template name="gl-direction">
        <xsl:with-param name="in-label">generation_forecast</xsl:with-param>
        <xsl:with-param name="out-label">scheduled_consumption</xsl:with-param>
      </xsl:call-template>
    </cell>
    <cell name="quantity_mw"><xsl:value-of select="entsoe:quantity"/></cell>
  </xsl:template>
</xsl:stylesheet>
