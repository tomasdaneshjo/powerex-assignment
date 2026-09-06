variable "region" {
  description = "AWS region to deploy into"
  type        = string
  default     = "eu-central-1"
}

variable "name_prefix" {
  description = "Prefix for resource names"
  type        = string
  default     = "powerex-scraper"
}

variable "api_key" {
  description = "API key exposed to the Lambda as the API_KEY environment variable (referenced from endpoints.json via env placeholders)."
  type        = string
  sensitive   = true
}

variable "lambda_zip" {
  description = "Path to the Lambda deployment package built by scripts/build.sh"
  type        = string
  default     = "../dist/lambda.zip"
}

variable "schedules" {
  description = "EventBridge Scheduler crons (Europe/Bratislava) and the endpoint ids each run invokes"
  type = map(object({
    cron         = string
    endpoint_ids = list(string)
  }))
  default = {
    forecast-evening = {
      cron         = "cron(30 17 * * ? *)"
      endpoint_ids = ["entsoe-generation-forecast-dayahead"]
    }
    actuals-morning = {
      cron         = "cron(30 9 * * ? *)"
      endpoint_ids = ["entsoe-generation-actual-perunit"]
    }
  }
}
