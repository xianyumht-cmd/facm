# 自签名证书与 CI 验证

GitHub Actions 使用自签名代码签名证书时，签名操作可以成功，但默认 Runner 不信任该证书，因此直接执行 `signtool verify /pa` 会报告证书链终止于不受信任的根证书。

FACM 构建流程只在一次性 Runner 的当前用户证书库中临时信任自签名公钥，用于验证 Authenticode 签名的完整性。PFX 私钥只写入 Runner 临时目录，签名后立即删除；Runner 结束后，临时证书库也会销毁。

这不代表外部 Windows 电脑会自动信任该自签名证书。对外正式发布仍应使用受信任代码签名机构签发的证书。
