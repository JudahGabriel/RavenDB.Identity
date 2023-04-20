Function GetNextNugetPackageVersion {

	$json = Invoke-RestMethod -Uri "https://api.nuget.org/v3/registration5-semver1/ravendb.identity/index.json"
	$package = $json.items[0]
	$latestPackageIndex = $package.items.Length - 1;
	$latestPackage = $package.items[$latestPackageIndex]
	$versionStr = $latestPackage.catalogEntry.version
	
	# Now that we have the version string ("8.0.7" or whatever), parse that and increment the Build part of it ("7" becomes "8" in this case)
	$versionObj = [version]::Parse($versionStr)
	$incrementedVersion = New-Object -TypeName System.Version -ArgumentList $versionObj.Major, $versionObj.Minor, ($versionObj.Build + 1)
	
    return $incrementedVersion.ToString()
}