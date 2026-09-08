<?xml version="1.0" encoding="UTF-8"?>
<!-- Shared traversal for ENTSO-E GL_MarketDocument responses.

     Emits one table row per Point (points sorted by position within their
     Period, series in document order) with the cells every GL document
     shares: timestamp_utc, resolution, area. Each endpoint stylesheet
     includes this file and contributes its own columns by defining

         <xsl:template match="entsoe:Point" mode="endpoint-cells">

     The gl-direction helper names a series' direction from the explicit
     in/outBiddingZone element; labels are parameterizable per endpoint. -->
<xsl:stylesheet version="1.0"
    xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
    xmlns:entsoe="urn:iec62325.351:tc57wg16:451-6:generationloaddocument:3:0"
    exclude-result-prefixes="entsoe">
  <xsl:include href="durations.xslt"/>
  <xsl:output method="xml" indent="yes"/>

  <xsl:template match="/">
    <table>
      <xsl:for-each select="entsoe:GL_MarketDocument/entsoe:TimeSeries/entsoe:Period">
        <xsl:apply-templates select="entsoe:Point" mode="gl-row">
          <xsl:sort select="number(entsoe:position)" data-type="number"/>
        </xsl:apply-templates>
      </xsl:for-each>
    </table>
  </xsl:template>

  <xsl:template match="entsoe:Point" mode="gl-row">
    <xsl:variable name="start">
      <xsl:call-template name="normalize-datetime">
        <xsl:with-param name="value" select="../entsoe:timeInterval/entsoe:start"/>
      </xsl:call-template>
    </xsl:variable>
    <row>
      <cell name="timestamp_utc">
        <xsl:call-template name="scaled-add">
          <xsl:with-param name="start" select="$start"/>
          <xsl:with-param name="duration" select="../entsoe:resolution"/>
          <xsl:with-param name="count" select="number(entsoe:position) - 1"/>
        </xsl:call-template>
      </cell>
      <cell name="resolution"><xsl:value-of select="../entsoe:resolution"/></cell>
      <cell name="area">
        <xsl:value-of select="../../entsoe:inBiddingZone_Domain.mRID
                              | ../../entsoe:outBiddingZone_Domain.mRID"/>
      </cell>
      <xsl:apply-templates select="." mode="endpoint-cells"/>
    </row>
  </xsl:template>

  <xsl:template name="gl-direction">
    <xsl:param name="in-label">generation</xsl:param>
    <xsl:param name="out-label">consumption</xsl:param>
    <xsl:choose>
      <xsl:when test="../../entsoe:inBiddingZone_Domain.mRID">
        <xsl:value-of select="$in-label"/>
      </xsl:when>
      <xsl:otherwise><xsl:value-of select="$out-label"/></xsl:otherwise>
    </xsl:choose>
  </xsl:template>
</xsl:stylesheet>
