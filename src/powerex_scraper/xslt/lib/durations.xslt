<?xml version="1.0" encoding="UTF-8"?>
<!-- Shared duration arithmetic for endpoint stylesheets (XSLT 1.0 + EXSLT).

     scaled-add: timestamp = start + count x duration, supporting the full
     ISO-8601 duration split the way XPath 2.0 defines it:
       - dayTime durations (PT15M, PT1H, P1D, ...) scale as seconds
       - yearMonth durations (P1M, P1Y, P1Y6M)     scale as total months
       - mixed forms (P1M1D) cannot be scalar-multiplied in any calendar -
         XPath 2.0 forbids xs:duration x number for the same reason - so
         they terminate loudly instead of producing wrong timestamps.
     EXSLT date:add performs the calendar-correct addition either way
     (month-end clamping included). -->
<xsl:stylesheet version="1.0"
    xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
    xmlns:date="http://exslt.org/dates-and-times"
    extension-element-prefixes="date">

  <xsl:template name="scaled-add">
    <xsl:param name="start"/>
    <xsl:param name="duration"/>
    <xsl:param name="count" select="0"/>

    <xsl:variable name="stepSeconds" select="date:seconds($duration)"/>
    <xsl:choose>
      <!-- dayTime family: date:seconds() succeeded (NaN = NaN is false) -->
      <xsl:when test="$stepSeconds = $stepSeconds">
        <xsl:value-of select="date:add($start, date:duration($count * $stepSeconds))"/>
      </xsl:when>
      <!-- yearMonth family: no time/day/week components, only Y and/or M -->
      <xsl:when test="not(contains($duration, 'T')) and not(contains($duration, 'D'))
                      and not(contains($duration, 'W'))">
        <xsl:variable name="body" select="substring-after($duration, 'P')"/>
        <xsl:variable name="years">
          <xsl:choose>
            <xsl:when test="contains($body, 'Y')">
              <xsl:value-of select="number(substring-before($body, 'Y'))"/>
            </xsl:when>
            <xsl:otherwise>0</xsl:otherwise>
          </xsl:choose>
        </xsl:variable>
        <xsl:variable name="monthPart">
          <xsl:choose>
            <xsl:when test="contains($body, 'Y')">
              <xsl:value-of select="substring-after($body, 'Y')"/>
            </xsl:when>
            <xsl:otherwise><xsl:value-of select="$body"/></xsl:otherwise>
          </xsl:choose>
        </xsl:variable>
        <xsl:variable name="months">
          <xsl:choose>
            <xsl:when test="contains($monthPart, 'M')">
              <xsl:value-of select="number(substring-before($monthPart, 'M'))"/>
            </xsl:when>
            <xsl:otherwise>0</xsl:otherwise>
          </xsl:choose>
        </xsl:variable>
        <xsl:variable name="totalMonths" select="($years * 12 + $months) * $count"/>
        <xsl:if test="not($totalMonths = $totalMonths) or ($years * 12 + $months) = 0">
          <xsl:message terminate="yes">unsupported resolution '<xsl:value-of select="$duration"/>'</xsl:message>
        </xsl:if>
        <xsl:value-of select="date:add($start, concat('P', $totalMonths, 'M'))"/>
      </xsl:when>
      <xsl:otherwise>
        <xsl:message terminate="yes">unsupported resolution '<xsl:value-of select="$duration"/>' - mixed calendar/time durations cannot be scaled</xsl:message>
      </xsl:otherwise>
    </xsl:choose>
  </xsl:template>

  <!-- normalize-datetime: the documents use minute precision (...T22:00Z);
       EXSLT date functions need full HH:MM:SS. -->
  <xsl:template name="normalize-datetime">
    <xsl:param name="value"/>
    <xsl:choose>
      <xsl:when test="string-length($value) = 17">
        <xsl:value-of select="concat(substring($value, 1, 16), ':00Z')"/>
      </xsl:when>
      <xsl:otherwise><xsl:value-of select="$value"/></xsl:otherwise>
    </xsl:choose>
  </xsl:template>
</xsl:stylesheet>
