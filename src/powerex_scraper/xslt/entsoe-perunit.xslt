<?xml version="1.0" encoding="UTF-8"?>
<!-- ENTSO-E A73/A16: actual generation per unit. The shared GL traversal
     emits timestamp_utc/resolution/area; this stylesheet adds the per-unit
     identity columns (names ship in-band in the native XML) and the value. -->
<xsl:stylesheet version="1.0"
    xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
    xmlns:entsoe="urn:iec62325.351:tc57wg16:451-6:generationloaddocument:3:0"
    exclude-result-prefixes="entsoe">
  <xsl:include href="lib/entsoe-gl-document.xslt"/>

  <xsl:template match="entsoe:Point" mode="endpoint-cells">
    <xsl:variable name="psr" select="../../entsoe:MktPSRType"/>
    <cell name="unit_eic"><xsl:value-of select="$psr/entsoe:PowerSystemResources/entsoe:mRID"/></cell>
    <cell name="unit_name"><xsl:value-of select="$psr/entsoe:PowerSystemResources/entsoe:name"/></cell>
    <cell name="production_type"><xsl:value-of select="$psr/entsoe:psrType"/></cell>
    <cell name="direction"><xsl:call-template name="gl-direction"/></cell>
    <cell name="quantity_mw"><xsl:value-of select="entsoe:quantity"/></cell>
  </xsl:template>
</xsl:stylesheet>
