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

variable "entsoe_base_url" {
  description = "ENTSO-E Transparency Platform host (the assignment's IOP environment by default)."
  type        = string
  default     = "https://iop-transparency.entsoe.eu"

  validation {
    condition     = startswith(var.entsoe_base_url, "https://")
    error_message = "entsoe_base_url must be an https URL."
  }
}

variable "lambda_zip" {
  description = "Path to the Lambda deployment package built by scripts/build.sh"
  type        = string
  default     = "../dist/lambda.zip"
}

variable "schedules" {
  description = "EventBridge Scheduler crons (Europe/Bratislava) and the dataset ids each run invokes"
  type = map(object({
    cron        = string
    dataset_ids = list(string)
  }))
  default = {
    forecast-evening = {
      cron        = "cron(30 17 * * ? *)"
      dataset_ids = ["generation-forecast-dayahead"]
    }
    actuals-morning = {
      cron        = "cron(30 9 * * ? *)"
      dataset_ids = ["generation-actual-perunit", "generation-actual-perunit-cz"]
    }
  }
}
