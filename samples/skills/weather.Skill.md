---
name: weather
description: Weather lookup tools powered by Open-Meteo (no API key required)
metadata:
  category: internet
runtime:
  type: http
  baseUrl: https://api.open-meteo.com
  timeoutSeconds: 15
  auth:
    type: none
  egress:
    allowHosts:
      - api.open-meteo.com
operations:
  - id: current
    summary: Get current weather for a latitude/longitude coordinate
    invoke:
      httpMethod: GET
      httpPath: /v1/forecast?latitude={latitude}&longitude={longitude}&current=temperature_2m,wind_speed_10m,weather_code
    parameters:
      latitude:
        type: number
        description: Latitude coordinate
        required: true
      longitude:
        type: number
        description: Longitude coordinate
        required: true
  - id: daily
    summary: Get daily weather forecast for a latitude/longitude coordinate
    invoke:
      httpMethod: GET
      httpPath: /v1/forecast?latitude={latitude}&longitude={longitude}&daily=weather_code,temperature_2m_max,temperature_2m_min&forecast_days={days}
    parameters:
      latitude:
        type: number
        description: Latitude coordinate
        required: true
      longitude:
        type: number
        description: Longitude coordinate
        required: true
      days:
        type: integer
        description: Number of forecast days (1-16)
        required: false
