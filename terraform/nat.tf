# fck-nat: NAT-instance alternative to the managed NAT Gateway (~$33/mo → ~$7/mo all-in).
# The module owns AMI lookup, ENI, source/dest check, SG, and the private default route.
module "fck_nat" {
  source  = "RaJiska/fck-nat/aws"
  version = "~> 1.6"

  name      = "${var.name_prefix}-nat"
  vpc_id    = aws_vpc.main.id
  subnet_id = aws_subnet.public.id

  instance_type = "t4g.nano" # default t4g.micro doubles the cost for zero benefit here
  ha_mode       = false      # single instance; an ASG is ceremony for an ephemeral demo
  # Nothing here uses Session Manager; keep the egress instance's surface minimal.
  attach_ssm_policy = false
  # No EIP: the auto-assigned public IP carries the identical $0.005/h IPv4 charge,
  # and ENTSO-E does not require a stable egress IP.

  update_route_tables = true
  route_tables_ids = {
    private = aws_route_table.private.id
  }
}
