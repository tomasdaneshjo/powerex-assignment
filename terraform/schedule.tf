# EventBridge Scheduler (not legacy CloudWatch rules): native IANA-timezone crons —
# DST transitions are AWS's problem — plus a per-schedule retry policy.
resource "aws_scheduler_schedule" "scrape" {
  for_each = var.schedules

  name = "${var.name_prefix}-${each.key}"

  schedule_expression          = each.value.cron
  schedule_expression_timezone = "Europe/Bratislava"

  flexible_time_window {
    mode = "OFF"
  }

  target {
    arn      = aws_lambda_function.scraper.arn
    role_arn = aws_iam_role.scheduler.arn

    input = jsonencode({ datasetIds = each.value.dataset_ids })

    # EventBridge Scheduler invokes Lambda asynchronously: this retry_policy covers
    # invocation-delivery failures only (throttling, service errors) — never a function that ran
    # and threw. A thrown ScrapeFailedException is retried by Lambda's own async-invocation
    # retry (2 attempts by default), which this resource does not configure.
    retry_policy {
      maximum_retry_attempts = 2
    }
  }
}
